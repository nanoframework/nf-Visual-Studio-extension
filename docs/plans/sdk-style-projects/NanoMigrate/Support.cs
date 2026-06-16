using System.Net.Http.Headers;
using System.Text.Json;

namespace NanoFramework.Migrate;

/// <summary>A user-facing error that prints cleanly without a stack trace.</summary>
internal sealed class UserError(string message) : Exception(message);

/// <summary>Per-repo outcome accumulated by the fleet command.</summary>
internal sealed class RepoReport
{
    public required string Name { get; init; }
    public int Projects { get; set; }
    public List<string> Review { get; } = new();
    public bool Committed { get; set; }
    public string? Error { get; set; }
}

/// <summary>Parsed command-line options shared across commands.</summary>
internal sealed class Options
{
    public string? Positional { get; private set; }
    public string Sdk { get; private set; } = "2.0.0";
    public string Tfm { get; private set; } = "netnano1.0";   // the TFM recognized by the .NET SDK / NuGet
    public string Ext { get; private set; } = ".nfproj";
    public bool DryRun { get; private set; }
    public bool NoBackup { get; internal set; }

    // clone
    public string Org { get; private set; } = "nanoframework";
    public string Filter { get; private set; } = "lib-";
    public string? Token { get; private set; } = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    public bool IncludeArchived { get; private set; }

    // fleet
    public string Report { get; private set; } = "migration-report.md";
    public string? Branch { get; private set; }
    public bool Commit { get; private set; }
    public string? CommitMessage { get; private set; }
    public string? Issue { get; private set; }     // referenced as "Fix #<n>" in the commit
    public bool SignOff { get; private set; } = true; // nanoFramework recommends Signed-off-by

    public static Options Parse(IEnumerable<string> args)
    {
        var o = new Options();
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            string Next(string name) => ++i < list.Count
                ? list[i]
                : throw new UserError($"{name} requires a value");

            switch (a)
            {
                case "--sdk": o.Sdk = Next(a); break;
                case "--tfm": o.Tfm = Next(a); break;
                case "--ext":
                    o.Ext = Next(a);
                    if (o.Ext is not (".nfproj" or ".csproj"))
                        throw new UserError("--ext must be .nfproj or .csproj");
                    break;
                case "--dry-run": case "--no-write": o.DryRun = true; break;
                case "--no-backup": o.NoBackup = true; break;
                case "--org": o.Org = Next(a); break;
                case "--filter": o.Filter = Next(a); break;
                case "--token": o.Token = Next(a); break;
                case "--include-archived": o.IncludeArchived = true; break;
                case "--report": o.Report = Next(a); break;
                case "--branch": o.Branch = Next(a); break;
                case "--commit": o.Commit = true; break;
                case "--message": o.CommitMessage = Next(a); break;
                case "--issue": o.Issue = Next(a).TrimStart('#'); break;
                case "--no-sign-off": o.SignOff = false; break;
                default:
                    if (a.StartsWith('-')) throw new UserError($"unknown option '{a}'");
                    if (o.Positional is not null) throw new UserError($"unexpected argument '{a}'");
                    o.Positional = a;
                    break;
            }
        }
        return o;
    }
}

/// <summary>Minimal GitHub REST client (BCL only) for listing org repositories.</summary>
internal static class GitHub
{
    internal sealed record Repo(string Name, string CloneUrl, bool Archived);

    public static List<Repo> ListOrgRepos(string org, string? token, bool includeArchived)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("nano-migrate", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var repos = new List<Repo>();
        for (int page = 1; ; page++)
        {
            var url = $"https://api.github.com/orgs/{org}/repos?per_page=100&page={page}&type=public";
            using var resp = http.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                var hint = resp.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? " (rate limited — pass --token or set GITHUB_TOKEN)" : "";
                throw new UserError($"GitHub API returned {(int)resp.StatusCode} {resp.StatusCode}{hint}");
            }

            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            if (arr.GetArrayLength() == 0) break;

            foreach (var e in arr.EnumerateArray())
            {
                var archived = e.TryGetProperty("archived", out var ar) && ar.GetBoolean();
                if (archived && !includeArchived) continue;
                repos.Add(new Repo(
                    e.GetProperty("name").GetString()!,
                    e.GetProperty("clone_url").GetString()!,
                    archived));
            }
            if (arr.GetArrayLength() < 100) break;
        }
        return repos;
    }
}
