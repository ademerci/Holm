<#
.SYNOPSIS
  Signs a release APK of Holm for Android with Holm's release key, with a signing lineage from the old debug key.

.DESCRIPTION
  Holm 1.0 and 1.1 were signed with a computer's debug key. From 1.2 on, Holm is signed with its own release key; the
  lineage (APK Signature Scheme v3.1 key rotation) tells Android that the new key follows the old one, so phones with
  1.1 accept 1.2 as an update instead of refusing it.

  The key lives outside the repository, in %LOCALAPPDATA%\HolmDev\keys (holm-release.jks, key.properties with its
  password, lineage.bin). Keep a copy of that folder somewhere safe: without the key no update can be published that
  installed apps accept.

.EXAMPLE
  ./tool/sign-release.ps1 build/app/outputs/flutter-apk/app-release.apk
#>
param(
    [Parameter(Mandatory)] [string]$Apk,
    [string]$Keys = "$env:LOCALAPPDATA\HolmDev\keys",
    [string]$OldKeystore = "$env:USERPROFILE\.android\debug.keystore"
)
$ErrorActionPreference = "Stop"

$props = @{}
foreach ($line in Get-Content "$Keys\key.properties") {
    if ($line -match '^\s*([^=]+)=(.*)$') { $props[$Matches[1].Trim()] = $Matches[2].Trim() }
}
$keystore = Join-Path $Keys $props.storeFile
$pass = $props.password
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "$env:LOCALAPPDATA\HolmDev\android-sdk" }
$tools = Get-ChildItem "$sdk\build-tools" | Sort-Object { [version]$_.Name } | Select-Object -Last 1
$apksigner = Join-Path $tools.FullName "apksigner.bat"
$lineage = Join-Path $Keys "lineage.bin"

# Once: the lineage from the debug key to the release key.
if (-not (Test-Path $lineage)) {
    & $apksigner rotate --out $lineage `
        --old-signer --ks $OldKeystore --ks-key-alias androiddebugkey --ks-pass pass:android `
        --new-signer --ks $keystore --ks-key-alias $props.alias --ks-pass "pass:$pass"
    if ($LASTEXITCODE -ne 0) { throw "apksigner rotate failed" }
}

& $apksigner sign `
    --ks $OldKeystore --ks-key-alias androiddebugkey --ks-pass pass:android `
    --next-signer --ks $keystore --ks-key-alias $props.alias --ks-pass "pass:$pass" `
    --lineage $lineage $Apk
if ($LASTEXITCODE -ne 0) { throw "apksigner sign failed" }
& $apksigner verify --print-certs $Apk | Select-String "Signer" | Select-Object -First 4
