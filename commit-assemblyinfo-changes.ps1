# Copyright (c) 2018 The nanoFramework project contributors
# See LICENSE file in the project root for full license information.

# skip updating assembly info changes if build is a pull-request or not a tag (master OR release)
if ($env:appveyor_pull_request_number -or
    ($env:APPVEYOR_REPO_BRANCH -eq "master" -and $env:APPVEYOR_REPO_TAG -eq 'true') -or
    ($env:APPVEYOR_REPO_BRANCH -match "^release*" -and $env:APPVEYOR_REPO_TAG -eq 'true') -or
    $env:APPVEYOR_REPO_TAG -eq "true")
{
    'Skip committing assembly info changes...' | Write-Host -ForegroundColor White
}
else
{
    # updated assembly info files   
    git add "source\VisualStudio.Extension\Properties\AssemblyInfo.cs"
    git add "source\VisualStudio.Extension\source.extension.vsixmanifest"
    git commit -m "Update versions for v$env:GitVersion_AssemblySemVer [skip ci]" -m"[version update]"
    git push origin --porcelain -q > $null
    
    'Updated version info...' | Write-Host -ForegroundColor White -NoNewline
    'OK' | Write-Host -ForegroundColor Green
}
