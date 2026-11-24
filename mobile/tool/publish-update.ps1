<#
.SYNOPSIS
  Puts a release APK of Holm for Android on Holm's update server; every installed Holm offers it within six hours.

.DESCRIPTION
  Reads the version from pubspec.yaml, signs the APK with Holm's release key (tool/sign-release.ps1), uploads it as
  holm-<name>-<code>.apk and then latest.json, or beta.json for a test version (versionCode, versionName, file,
  sha256, notes) into /var/www/holm, served by nginx over HTTPS on port 8473. Phones that have "Test versions" on get
  whichever of the two is newer.
  The app pins that server's certificate (lib/update.dart) and checks the APK's SHA-256 before installing it.
  Raise the version (the number after +) for every upload: the app installs only a higher versionCode.
  Build with the same signing key each time, or Android refuses the update.

.EXAMPLE
  flutter build apk --release
  ./tool/publish-update.ps1 -Notes "Corners, muting apps and updates"
  ./tool/publish-update.ps1 -Channel beta -Notes "Try the new overview"
#>
param(
    [string]$Apk = "build/app/outputs/flutter-apk/app-release.apk",
    [string]$Notes = "",
    [ValidateSet("stable", "beta")] [string]$Channel = "stable",
    [switch]$NoSign,
    [string]$Server = "root@62.83.85.236",
    [string]$Folder = "/var/www/holm"
)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot)

$line = (Select-String -Path pubspec.yaml -Pattern '^version:\s*(\d+\.\d+\.\d+)\+(\d+)').Matches[0]
if (-not $line) { throw "No version like 1.2.3+4 in pubspec.yaml" }
$name = $line.Groups[1].Value
$code = [int]$line.Groups[2].Value
if (-not (Test-Path $Apk)) { throw "No APK at $Apk (flutter build apk --release)" }

if (-not $NoSign) { & "$PSScriptRoot/sign-release.ps1" $Apk | Out-Host }

$file = "holm-$name-$code.apk"
$index = if ($Channel -eq "beta") { "beta.json" } else { "latest.json" }
$sha = (Get-FileHash $Apk -Algorithm SHA256).Hash.ToLowerInvariant()
$json = [ordered]@{ versionCode = $code; versionName = $name; file = $file; sha256 = $sha; notes = $Notes } | ConvertTo-Json -Compress
$tmp = Join-Path ([IO.Path]::GetTempPath()) "holm-$index"
[IO.File]::WriteAllText($tmp, $json, [Text.UTF8Encoding]::new($false))

# The APK first, then the index in one move: a check never sees a version whose file isn't there yet.
scp $Apk "${Server}:$Folder/$file"
scp $tmp "${Server}:$Folder/$index.new"
ssh $Server "chmod 644 $Folder/$file $Folder/$index.new && mv $Folder/$index.new $Folder/$index"
Remove-Item $tmp
Write-Host "Published Holm $name ($code) as $Channel ($index): $file, sha256 $sha"
