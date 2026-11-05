param(
    [string]$Publisher = 'CN=Holm Local Test',
    [string]$Identity = 'Holm.LocalTest',
    [string]$Version,
    [switch]$Store
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($Store -and -not $Version) { $Version = '1.7.9.0' }
if (-not $Version) {
    [xml]$project = Get-Content (Join-Path $root 'WinNotch\WinNotch.csproj') -Raw
    $releaseVersion = [string]$project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    if ($releaseVersion -notmatch '^[1-9][0-9]*\.[0-9]+\.[0-9]+$') { throw 'Project version must have three numeric components.' }
    $Version = $releaseVersion + '.0'
}
if ($Store) {
    $Identity = 'HolmTeam.HolmforWindows'
    $Publisher = 'CN=D6BE962E-E0E5-4718-93AF-960659970417'
    if ($Version -notmatch '^([1-9][0-9]*)\.([0-9]+)\.([0-9]+)\.0$') { throw 'Store package version must have four components and end in .0.' }
}
$displayName = if ($Store) { 'Holm for Windows' } else { 'Holm Local Test' }
$publisherDisplayName = if ($Store) { 'Holm Team' } else { 'Holm Local Test' }
$packageName = if ($Store) { 'Holm-Store.msix' } else { 'Holm-test.msix' }
$out = Join-Path $root 'artifacts\msix'
$app = Join-Path $out 'layout'
if (Test-Path -LiteralPath $app) { Remove-Item -LiteralPath $app -Recurse -Force }
New-Item -ItemType Directory -Force $app, (Join-Path $app 'Assets') | Out-Null
$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (!$makeappx) { throw 'Install the Windows SDK including MakeAppx.' }
dotnet publish (Join-Path $root 'WinNotch\WinNotch.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $app
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Get-ChildItem -LiteralPath $app -Filter *.pdb | Remove-Item -Force
Add-Type -AssemblyName System.Drawing
# Generate each size from the original 2048px artwork, never from a smaller icon frame.
$artwork = [System.Drawing.Image]::FromFile((Join-Path $root 'docs\icon.png'))
try {
    if ($artwork.Width -ne $artwork.Height -or $artwork.Width -lt 600) { throw 'Holm artwork must be square and at least 600 pixels.' }
    function Save-Logo([int]$pixels, [string]$path) {
        $bitmap = New-Object System.Drawing.Bitmap $pixels, $pixels, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($artwork, 0, 0, $pixels, $pixels)
            } finally { $graphics.Dispose() }
            $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    }
    foreach ($size in @(44, 150, 50)) {
        Save-Logo $size (Join-Path $app "Assets\Logo$size.png")
        foreach ($scale in @(125, 150, 200, 400)) {
            $pixels = [int][Math]::Round($size * $scale / 100.0, [MidpointRounding]::AwayFromZero)
            Save-Logo $pixels (Join-Path $app "Assets\Logo$size.scale-$scale.png")
        }
    }
    # Optional Partner Center listing icon; the manifest assets above are also fixed.
    Save-Logo 300 (Join-Path $out 'Holm-Store-tile-300.png')
} finally { $artwork.Dispose() }
$escapedPublisher = [System.Security.SecurityElement]::Escape($Publisher)
$escapedIdentity = [System.Security.SecurityElement]::Escape($Identity)
$escapedVersion = [System.Security.SecurityElement]::Escape($Version)
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10" IgnorableNamespaces="uap uap3 rescap desktop">
  <Identity Name="$escapedIdentity" Publisher="$escapedPublisher" Version="$escapedVersion" ProcessorArchitecture="x64" />
  <Properties><DisplayName>$displayName</DisplayName><PublisherDisplayName>$publisherDisplayName</PublisherDisplayName><Logo>Assets\Logo50.png</Logo></Properties>
  <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
  <Resources><Resource Language="en-us" /></Resources>
  <Applications><Application Id="Holm" Executable="Holm.exe" EntryPoint="Windows.FullTrustApplication"><uap:VisualElements DisplayName="$displayName" Description="Holm desktop companion" BackgroundColor="transparent" Square150x150Logo="Assets\Logo150.png" Square44x44Logo="Assets\Logo44.png" /><Extensions><desktop:Extension Category="windows.startupTask" Executable="Holm.exe" EntryPoint="Windows.FullTrustApplication"><desktop:StartupTask TaskId="HolmStartup" Enabled="false" DisplayName="Holm" /></desktop:Extension></Extensions></Application></Applications>
  <Capabilities><uap3:Capability Name="userNotificationListener" /><rescap:Capability Name="runFullTrust" /><DeviceCapability Name="location" /><DeviceCapability Name="radios" /></Capabilities>
</Package>
"@
[IO.File]::WriteAllText((Join-Path $app 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
& $makeappx.FullName pack /d $app /p (Join-Path $out $packageName) /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx failed.' }
Write-Host "Unsigned package: $out\$packageName"
