# generate change log when build is NOT for a pull-request
if ($env:appveyor_pull_request_number)
{
    'Skip change log processing as this is a PR build...' | Write-Host -ForegroundColor White
}
else
{
    # need this to keep ruby happy
    md c:\tmp

    # generate change log
    # version includes commits
    bundle exec github_changelog_generator --token $env:GitHubToken

    # updated changelog and the updated assembly info files
    git add CHANGELOG.md
    git commit -m "Update CHANGELOG for v$env:GitVersion_AssemblySemVer"
    git push origin --porcelain -q > $null
}
