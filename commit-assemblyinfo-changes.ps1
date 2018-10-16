# only need to commit assembly info changes when build is NOT for a pull-request
if ($env:appveyor_pull_request_number)
{
    'Skip committing assembly info changes as this is a PR build...' | Write-Host -ForegroundColor White
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
