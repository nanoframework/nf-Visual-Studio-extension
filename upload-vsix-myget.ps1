# skip pull requests
if ($env:APPVEYOR_PULL_REQUEST_NUMBER)
{
    return
}

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
