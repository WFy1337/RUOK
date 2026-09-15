#Requires -Version 7
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [switch]$AllowDirty
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$project = Join-Path $root 'src\RUOK.App\RUOK.App.csproj'
$revision = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
$changes = & git -C $root status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the source worktree.' }
if ($changes -and -not $AllowDirty) {
    throw 'Release builds require committed source. Use -AllowDirty only for local development checks.'
}

$output = Join-Path $root "artifacts\releases\v$Version"
$stage = Join-Path $output ('publish-' + [Guid]::NewGuid().ToString('N'))
$notices = Join-Path $output 'ThirdPartyNotices'
$support = Join-Path $output 'runtime-support'
New-Item -ItemType Directory -Path $stage, $notices, $support -Force | Out-Null

& dotnet restore $project -p:Configuration=Release -p:RUOKStandalone=true --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Standalone dependency restore failed.' }

$assets = Get-Content -LiteralPath (Join-Path $root 'src\RUOK.App\obj\Standalone\project.assets.json') -Raw |
    ConvertFrom-Json -AsHashtable
$packages = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($library in $assets.libraries.GetEnumerator()) {
    if ($library.Value.type -eq 'package') {
        $packages[$library.Key] = $library.Value.path.Replace('/', '\')
    }
}
foreach ($framework in $assets.project.frameworks.Values) {
    foreach ($download in $framework.downloadDependencies) {
        $range = [regex]::Match($download.version, '^\[([^,]+),\s*\1\]$')
        if (-not $range.Success) { throw "Expected an exact SDK package version: $($download.name)" }
        $package = "$($download.name)/$($range.Groups[1].Value)"
        $packages[$package] = $package.ToLowerInvariant().Replace('/', '\')
    }
}

$index = [System.Collections.Generic.List[string]]::new()
$index.Add('RUOK third-party dependency notices')
$index.Add('Includes the resolved SDK/build dependencies as well as redistributed application/runtime components.')
$index.Add('Upstream license and notice files are preserved in the component directories.')
$index.Add('The .NET runtime LICENSE.TXT and THIRD-PARTY-NOTICES.TXT also contain the full MIT and Apache 2.0 terms.')
$index.Add('')
$runtimePackage = $null
foreach ($package in ($packages.Keys | Sort-Object)) {
    $folder = $null
    foreach ($cache in $assets.packageFolders.Keys) {
        $candidate = Join-Path $cache $packages[$package]
        if (Test-Path -LiteralPath $candidate -PathType Container) { $folder = $candidate; break }
    }
    if (-not $folder) { throw "Restored dependency is missing: $package" }
    if ($package.StartsWith('Microsoft.WindowsAppSDK.Runtime/', [StringComparison]::OrdinalIgnoreCase)) {
        $runtimePackage = $folder
    }
    $spec = @(Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' -File)
    if ($spec.Count -ne 1) { throw "Expected one NuGet manifest: $package" }
    [xml]$xml = Get-Content -LiteralPath $spec[0].FullName -Raw
    $metadata = $xml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
    $index.Add($package)
    foreach ($name in @('authors', 'copyright', 'license', 'licenseUrl')) {
        $element = $metadata.SelectSingleNode("*[local-name()='$name']")
        if ($null -ne $element) { $index.Add("  ${name}: $($element.InnerText)") }
    }
    $files = @(Get-ChildItem -LiteralPath $folder -File |
        Where-Object Name -Match '^(license|notice|copying|third.?party.?notices)')
    $license = $metadata.SelectSingleNode('*[local-name()="license"]')
    if ($null -ne $license -and $license.GetAttribute('type') -eq 'file') {
        $licensePath = [IO.Path]::GetFullPath((Join-Path $folder $license.InnerText))
        if (-not $licensePath.StartsWith($folder.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "License path escapes its dependency directory: $package"
        }
        $files += Get-Item -LiteralPath $licensePath
    }
    $destination = Join-Path $notices $packages[$package]
    foreach ($file in ($files | Sort-Object FullName -Unique)) {
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    $index.Add('')
}
[IO.File]::WriteAllLines((Join-Path $notices 'INDEX.txt'), $index, [Text.UTF8Encoding]::new($false))
$runtimeNotices = Join-Path $notices 'microsoft.netcore.app.runtime.win-x64'
if (-not (Test-Path -LiteralPath $runtimeNotices -PathType Container)) {
    throw 'The self-contained .NET runtime notices were not collected.'
}

# This SDK's self-contained targets omit the resource DLL required by unpackaged notifications.
if (-not $runtimePackage) { throw 'The locked Windows App SDK runtime package was not resolved.' }
$archives = @(Get-ChildItem -LiteralPath (Join-Path $runtimePackage 'tools\MSIX\win10-x64') -File |
    Where-Object Name -Match '^Microsoft\.WindowsAppRuntime\.\d+\.msix$')
if ($archives.Count -ne 1) { throw 'Expected one x64 framework archive in the locked runtime package.' }
$archive = [IO.Compression.ZipFile]::OpenRead($archives[0].FullName)
try {
    $name = 'Microsoft.WindowsAppRuntime.Insights.Resource.dll'
    $entry = $archive.GetEntry($name)
    if ($null -eq $entry) { throw "The locked runtime archive does not contain $name" }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $support $name), $true)
} finally {
    $archive.Dispose()
}

& dotnet publish $project --configuration Release --no-restore -p:RUOKStandalone=true `
    "-p:Version=$Version" "-p:SourceRevisionId=$revision" "-p:RUOKNoticesPath=$notices" "-p:RUOKSupportPath=$support" --output $stage
if ($LASTEXITCODE -ne 0) { throw "Standalone publish failed; inspect $stage" }
$files = @(Get-ChildItem -LiteralPath $stage -Force)
if ($files.Count -ne 1 -or $files[0].PSIsContainer -or $files[0].Name -ne 'RUOK.App.exe') {
    throw "Expected exactly one executable without sidecars; inspect $stage"
}

$executable = Join-Path $output "RUOK-v$Version-win-x64.exe"
Copy-Item -LiteralPath $files[0].FullName -Destination $executable -Force
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = $executable + '.sha256'
[IO.File]::WriteAllText($checksum, "$hash *$([IO.Path]::GetFileName($executable))`n", [Text.UTF8Encoding]::new($false))
Remove-Item -LiteralPath $files[0].FullName
Remove-Item -LiteralPath $stage
[pscustomobject]@{
    Executable = $executable
    Bytes = (Get-Item -LiteralPath $executable).Length
    Sha256 = $hash
    ChecksumFile = $checksum
    SourceCommit = $revision
    UncommittedSource = [bool]$changes
    Signed = (Get-AuthenticodeSignature -LiteralPath $executable).Status -eq 'Valid'
} | ConvertTo-Json
