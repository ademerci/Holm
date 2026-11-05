# Local MSIX testing

Open PowerShell in the repository root (the folder containing `packaging`), then run `powershell -ExecutionPolicy Bypass -File .\packaging\build-msix.ps1`. This needs the .NET 10 SDK and Windows SDK (MakeAppx). The output is `artifacts/msix/Holm-test.msix`, an unsigned, self-contained x64 test package. Its identity and artwork are placeholders, not Store submission metadata.

For a local test, create a code-signing certificate, trust its public certificate on the test machine, and sign the package. Trusting a certificate changes Windows security settings; only trust a certificate you created and control. Open an elevated PowerShell in the repository root, then run:

```powershell
$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=Holm Local Test' -KeyUsage DigitalSignature -FriendlyName 'Holm local MSIX test' -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
Export-Certificate -Cert $cert -FilePath "$PWD\artifacts\msix\Holm-test.cer"
Import-Certificate -FilePath "$PWD\artifacts\msix\Holm-test.cer" -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
& $signtool.FullName sign /fd SHA256 /sha1 $cert.Thumbprint artifacts\msix\Holm-test.msix
Add-AppxPackage artifacts\msix\Holm-test.msix
```

The build alone does not install the package. Verify installation with `Get-AppxPackage -Name Holm.LocalTest`; if it returns nothing, check the output of `Add-AppxPackage` for an error. After a successful installation, launch **Holm Local Test** from Start. Close any running portable Holm first. Test launch, settings, notifications, media, Beam, privacy indicators, and uninstall/reinstall. Use Settings > Start with Windows to enable the packaged startup task, then sign out and back in to verify it. If Windows has disabled Holm in Startup apps, re-enable it there. Packaged updates are managed by Windows instead of Holm's GitHub updater. Test package data may be isolated from portable settings and removed on uninstall. Remove the test package through Windows Settings, and remove the test certificate from Trusted People when finished. Do not distribute the test certificate or its private key.

The default build uses a local test identity and cannot be submitted to the Store.



For an upgrade, build with a higher four-part -Version, sign using the same certificate (do not create a new one), and install the signed package. Rebuilding overwrites the signed package with an unsigned one.


## Store package

Run `powershell -ExecutionPolicy Bypass -File .\packaging\build-msix.ps1 -Store` to create `artifacts\msix\Holm-Store.msix` using the reserved Holm for Windows identity and Holm Team publisher. The current Store package version is 1.7.9.0 (the application itself is 1.7.8); it was incremented for the resubmission. For each subsequent Store release, update the default Store version in the script to a higher version, with the fourth component set to zero. The local test package defaults to the project version. This package is unsigned for Partner Center; do not sign it with the local test certificate. The Store supplies its own signature.

Before submitting, run Windows App Certification Kit on the current packaged build, investigate warnings, test startup after sign-in, notifications, Bluetooth, weather, Beam and updates, and verify upgrade and settings migration on a clean machine. Complete the Store listing, privacy policy, screenshots, logo, age rating and pricing in Partner Center. The generated package logos are derived from the existing icon and need visual review.

The build also creates `artifacts\msix\Holm-Store-tile-300.png` for the optional 300 × 300 Store listing icon. Package logos at multiple display scales are generated from `docs\icon.png`.
