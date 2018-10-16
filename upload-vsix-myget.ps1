# skip upload on pull requests
if ($env:APPVEYOR_PULL_REQUEST_NUMBER)
{
    'Skip upload to MyGet, this is a PR build...' | Write-Host -ForegroundColor White
    return
}

# skip upload when this is not a tag
if ($env:appveyor_repo_tag -eq "true")
{

    $artifactsSearchPattern = "./*.vsix"
    $artifactsCollection = (Get-ChildItem $artifactsSearchPattern -Recurse)

    $vsixMyGetUploadEndpoint = "https://www.myget.org/F/nanoframework-dev/vsix/upload"

    # for the environment variable MyGetToken to work here it has to be set in AppVeyor UI

    foreach($file in $artifactsCollection)
    {
        'Uploading VSIX package to MyGet feed...' | Write-Host -ForegroundColor White -NoNewline

        $webClient = New-Object System.Net.WebClient
        $webClient.Headers.add('X-NuGet-ApiKey', $env:MyGetToken)
        $webClient.UploadFile($vsixMyGetUploadEndpoint, 'POST', $file)
        'OK' | Write-Host -ForegroundColor Green
    }
}
else 
{
    'Skip upload to MyGet, this commit is not a tag commit...' | Write-Host -ForegroundColor White
}