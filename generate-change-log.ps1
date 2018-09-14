# generate cahnge log on tag commit
if ($env:appveyor_repo_tag -eq "true")
{
    # need this to keep ruby happy
    md c:\tmp

    # generate change log
    bundle exec github_changelog_generator --token $env:GitHubToken --future-release "v$env:GitVersion_MajorMinorPatch.$env:GitVersion_CommitsSinceVersionSource"

    # updated changelog and the updated assembly info files
    git add CHANGELOG.md
    git commit -m "Update CHANGELOG for v$env:GitVersion_MajorMinorPatch.$env:GitVersion_CommitsSinceVersionSource"
    # need to wrap the git command bellow so it doesn't throw an error because of redirecting the output to stderr
    "$(git push origin)"
}
