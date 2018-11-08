# Copyright (c) 2018 The nanoFramework project contributors
# See LICENSE file in the project root for full license information.

# skip generating the change log when build is a pull-request or not a tag (can't commit when repo is in a tag)
if ($env:appveyor_pull_request_number -or $env:APPVEYOR_REPO_TAG -eq "true")
{
    'Skip change log processing...' | Write-Host -ForegroundColor White
}
else
{
    # need this to keep ruby happy
    md c:\tmp

    if ($env:APPVEYOR_REPO_BRANCH -eq "master" -or $env:APPVEYOR_REPO_BRANCH -match "^release*")
    {
        # generate change log including future version
        bundle exec github_changelog_generator --token $env:GitHubToken --future-release "v$env:NBGV_Version"
    }
    else 
    {
        # generate change log
        # version includes commits
        bundle exec github_changelog_generator --token $env:GitHubToken        
    }

    # updated changelog, if there are any differences
    $logDif = git diff CHANGELOG.md

    if($logDif -ne $null)
    {
        git add CHANGELOG.md
        git commit -m "Update CHANGELOG for v$env:MyNuGetVersion"
        # need to wrap the git command bellow so it doesn't throw an error because of redirecting the output to stderr
        git push origin "HEAD:$env:APPVEYOR_REPO_BRANCH" --porcelain  | Write-Host
    }
}
