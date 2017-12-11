if ($env:APPVEYOR_PULL_REQUEST_NUMBER)
{
    # version for pull requests
    $publishVersion = $Env:GitVersion_MajorMinorPatch+"."+$Env:GitVersion_CommitsSinceVersionSource     
}
else 
{
    # version for commits other than PRs
    $publishVersion = $Env:GitVersion_AssemblySemVer    
}

# Regular expression pattern to find the version in the build number 
# and then apply it to the assemblies
$versionRegex = "\d+\.\d+\.\d+\.\d+"

$vsixManifestSearchPattern = "./source.extension.vsixmanifest"
$vsixManifestCollection = (Get-ChildItem $vsixManifestSearchPattern -Recurse)

foreach($file in $vsixManifestCollection)
{
    $filecontent = Get-Content($file)
    attrib $file -r
    $filecontent -replace $versionRegex, $publishVersion | Out-File $file -Encoding utf8
    "Update version in VSIX manifest to $publishVersion"| Write-Host -ForegroundColor White
}
