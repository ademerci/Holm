# ROAST BRIEF – Holm

## Zweck (3 Sätze)

Holm ist eine "Dynamic Island" für Windows: eine schwarze Notch oben am Bildschirm, die zeigt, was gerade läuft
(Musik, Anrufe, Downloads, Nachrichten, Akku) und beim Überfahren mit der Maus aufklappt. Offen bietet sie
Musiksteuerung und kleine Werkzeuge (Notiz, Erinnerungen, Rechner, Zwischenablage, Lautstärke pro App, Farbpipette).
Dazu kommen "Beam" (Dateien an Freunde, im Netz oder per Code senden), Chat mit Freunden und ein Holm-Fenster mit
Einstellungen.

## Zielgruppe

Windows-10/11-Nutzer, die eine schlanke, schöne Statusanzeige wollen (oft mit Musik, Discord/Teams, mehreren
Monitoren). Desktop mit Maus und Tastatur. Englische Oberfläche; Erinnerungen verstehen auch Deutsch.

## Wie testen

- **Keine URL, kein Konto.** Holm ist eine Desktop-App (WPF, .NET 10), keine Web-App. Es gibt keinen Login.
- **Nicht live testbar für dich:** Die App läuft auf dem PC des Nutzers als Einzelinstanz und würde seine Maus
  stören. Du arbeitest mit **Screenshots** und **Code**. Schreibe in deinem Bericht den Satz: **"nicht live getestet"**.
- **Screenshots** (Grösse "Normal", 96 DPI, mit Demo-Daten): `C:\Avtoris\02 Projects\04 Software\Holm\docs\opt\before\*.png`
  (70 Bilder). Wichtige: `idle.png`, `compact.png`, `expanded.png`, `expanded-today.png`, `expanded-notes.png`,
  `expanded-reminders.png`, `expanded-calculator.png`, `expanded-clipboard.png`, `expanded-mixer.png`,
  `expanded-weather-place.png`, `peek.png`, `volume.png`, `message.png`, `reminder.png`, `airdrop-share.png`,
  `airdrop-ask.png`, `screenshot.png`, `settings.png`, `settings-popouts.png`, `settings-profile.png`,
  `hub-chat.png`, `hub-add.png`, `hub-activity.png`, `hub-achievements.png`, `timer.png`, `call.png`, `usb.png`.
- **Code:** `C:\Avtoris\02 Projects\04 Software\Holm\WinNotch\` – `MainWindow.xaml` (Notch-Layout, Rechtsklick-Menü),
  `MainWindow.*.cs` (je Feature), `SettingsWindow.cs` (Holm-Fenster), `ChatPage.cs`, `AddFriendPage.cs`,
  `ProfileEditor.cs`, `NetworkPage.cs`, `HubTheme.cs` (Farben/Stile). Nutzerdoku: `README.md`.
- Testdaten: Die Screenshots zeigen Demo-Daten ("Alex Muster", "Blinding Lights", Erinnerungen, Clipboard-Einträge).
  Leere Zustände sind nur im Code sichtbar (z. B. Platzhaltertexte).

## Abläufe (aus FLOWS.md, K = Klicks)

| # | Ablauf | Schritte |
|---|---|---|
| F1 | Notch öffnen, Musik steuern | Maus auf Notch (140 ms) oder Klick → Play/Pause, Vor, Zurück, Seek, Lautstärke |
| F2 | Notiz | Notch öffnen → Notiz-Icon → tippen (speichert selbst) |
| F3 | Erinnerung | Notch öffnen → Glocke → "20 min laundry" → Enter |
| F5 | Rechnen | Notch öffnen → Rechner → Summe tippen → Ergebnis klicken = kopiert |
| F6 | Zwischenablage | Notch öffnen → Clipboard-Icon → Eintrag klicken |
| F7 | Lautstärke pro App | Notch öffnen → Mixer → Slider / Icon (stumm) |
| F9 | Timer | Rechtsklick → Timer → Dauer |
| F11 | Datei beamen | Datei auf Notch ziehen → Freund/Nearby/Phone/Code wählen → Fortschritt |
| F13 | Screenshot | Win+Shift+S → Pop-out → Copy text / Shelf / Send |
| F15 | Einstellung ändern | Rechtsklick → Settings… → Seite → Schalter → Apply (Esc schliesst) |
| F16 | Wetter-Ort | Rechtsklick → Customize → Weather → Choose the place… → Stadt → Enter |
| F17 | Freund hinzufügen | Holm-Fenster → Add a friend → Code teilen/einfügen |
| F18 | Chatten | Holm-Fenster → Chat → Freund → tippen → Enter |
| F19 | Wi-Fi und Speedtest | Rechtsklick → Wi-Fi and speed test… → Start (~15 s) |
| F21 | Profil | Holm-Fenster → Profil → Name/Status/Farbe/Bild → Apply |
| F22 | Pop-outs | Lautstärke, Kopfhörer, Caps Lock, USB, Download, Anruf, VPN, Nachricht – erscheinen von selbst |

## Was Holm bewusst NICHT kann / nicht will (nicht als Mangel melden)

- Kein Konto, keine Cloud, kein Login; Freunde über Code-Austausch, Nachrichten über ntfy (Ende-zu-Ende verschlüsselt).
- Kein Mobile, kein macOS/Linux. Keine Touch-Optimierung.
- Keine eigene Musikwiedergabe; steuert nur vorhandene Player (Spotify, Browser, VLC).
- Speedtest startet nie von selbst (Datenverbrauch).
- Einstellungen wirken erst nach "Apply" (Entwurf + Zurücksetzen gewollt).
- Die Notch nimmt nie ungefragt den Fokus; Tastatur nur in Textfeldern.
- Oberfläche nur Englisch.
- Keine Werbung, keine Telemetrie.
