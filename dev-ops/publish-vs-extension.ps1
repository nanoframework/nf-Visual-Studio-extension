#  Copyright (c) 2019 The nanoFramework project contributors
#  See LICENSE file in the project root for full license information.

$marketplaceToken = $args[0]
$VsixPath = $args[1]
$ManifestPath = $args[2]

# Find the location of VsixPublisher
$Installation = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -format json | ConvertFrom-Json
$Path = $Installation.installationPath

Write-Host $Path
$VsixPublisher = Join-Path -Path $Path -ChildPath "VSSDK\VisualStudioIntegration\Tools\Bin\VsixPublisher.exe" -Resolve

Write-Host $VsixPublisher

# Publish to VSIX to the marketplace
& $VsixPublisher publish -payload $VsixPath -publishManifest $ManifestPath -personalAccessToken $marketplaceToken -ignoreWarnings "VSIXValidatorWarning01,VSIXValidatorWarning02,VSIXValidatorWarning08"
