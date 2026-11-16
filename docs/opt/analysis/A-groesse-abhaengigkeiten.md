# Analyse A – Grösse und Abhängigkeiten (Subagent, nur lesend)

| Fund | Beleg | Wirkung | Aufwand | Risiko |
|---|---|---|---|---|
| Ungenutzte WPF-Assemblies im Bundle (Ribbon, Classic/Luna/Royale-Themes, ReachFramework/Druck) per Publish-Filter entfernen | RuntimeList.xml Profile=WPF; grep Ribbon im Code 0 Treffer | geschätzt 5-10 MB (nicht gemessen) | S | mittel |
| Prüfen ob WinForms-DLLs im Bundle landen | csproj ohne UseWindowsForms | 0 oder ~50 MB | S (messen) | niedrig |
| SatelliteResourceLanguages de;en -> prüfen, ob Satelliten gefiltert werden | WinNotch.csproj:13 | 0-3 MB | S | niedrig |
| Microsoft.Windows.UI.Xaml.dll (7.3 MB IL) wird evtl. R2R-kompiliert, ungenutzt | WinNotch.csproj:50 (nur SDK.NET.dll excluded) | 10-18 MB wenn R2R | S | niedrig-mittel |
| Partial Trimming BCL | – | unbelegt 30-60 MB | L | hoch – nicht tun |
| Emoji.Wpf behalten (Farb-Emoji im Chat) | ChatPage.cs:391,445,600,601,745 | – | L | hoch |
| QRCoder durch eigenen Encoder ersetzen | QrCode.cs:14, MainWindow.Phone.cs:64 | 0.5-1 MB | M | mittel – lohnt kaum |
| notch.ico (381 KB) doppelt (ApplicationIcon + Resource) | WinNotch.csproj:44,52; SettingsWindow.cs:82 | 0.2-0.4 MB | S | niedrig |
| FieldTemplate() XAML-String doppelt | HubTheme.cs:163-175, ProfileEditor.cs:222-234 | Wartbarkeit | S | niedrig |
| Vier Freeze-Helfer, doppelte Farben | Controls.cs:22-27, HubTheme.cs:23-29 | Wartbarkeit | S | niedrig |
| 16 P/Invoke doppelt deklariert | ColorPicker.cs:49, ScreenCapture.cs:109, Shell.cs:58 … | Wartbarkeit | M | niedrig |
| 6-7 HttpClient-Instanzen | Gifs.cs:281,547 … | unbelegt | M | mittel |
| Dead-Code-Scan: nichts gefunden | 586 Methoden geprüft | 0 | – | – |
| DEBUG/BENCH-Code leckt nicht in Release | #if DEBUG Blöcke | 0 | – | – |
| InvariantGlobalization/UseSystemResourceKeys nicht empfohlen | kein ICU im Bundle | 0 | – | hoch |

Top 3 (Agent): Bundle-Inhalt messen (WinForms? UI.Xaml? Satelliten?); Publish-Filter; Icon + Duplikate.
