# FLOWS – Nutzerabläufe von Holm

Holm ist eine WPF Desktop App (Windows, .NET 10): eine "Dynamic Island" Notch am oberen Bildschirmrand plus ein
Fenster ("Holm window") für Einstellungen, Freunde und Chat. Es gibt keine Konten, keine Web Routen, keine Datenbank.
Daten liegen als JSON in `%APPDATA%\Holm` (Test: `HOLM_DATA`, siehe DECISIONS.md).

Zählweise: **K** = Klicks (inkl. Rechtsklick, Menüpunkte), **E** = Eingabefelder, **W** = Wartezeiten, die der Nutzer
merkt. Hover zählt nicht als Klick. Quelle: Code (Dateiname) und `--snap` Screenshots (`docs/opt/before/`).

| # | Ablauf | Schritte | K | E | W | Code |
|---|---|---|---|---|---|---|
| F1 | Notch öffnen und Musik steuern | Maus auf Notch (Hover-Verzögerung) oder Klick → offene Notch → Play/Pause, Vor, Zurück, Seek, Lautstärke | 1–2 | 0 | Hover-Delay | MainWindow.xaml.cs `Island_MouseEnter`, `Play_Click` |
| F2 | Notiz schreiben | Notch öffnen → Notiz-Icon → tippen (wird automatisch gespeichert) | 2 | 1 | 0 | MainWindow.Notes.cs |
| F3 | Erinnerung anlegen | Notch öffnen → Glocke → "20 min laundry" tippen → Enter | 2 | 1 | 0 | MainWindow.Reminders.cs `ReminderBox_KeyDown` |
| F4 | Erinnerung fällig | Notch klingelt → "+10 min" oder OK | 1 | 0 | 0 | MainWindow.Reminders.cs |
| F5 | Rechnen | Notch öffnen → Rechner → Summe tippen → Ergebnis klicken = kopiert | 3 | 1 | 0 | MainWindow.Calculator.cs |
| F6 | Zwischenablage zurückholen | Notch öffnen → Clipboard-Icon → Eintrag klicken | 3 | 0 | 0 | MainWindow.Clipboard.cs |
| F7 | Lautstärke pro App | Notch öffnen → Mixer-Icon → Slider ziehen / Icon klicken (stumm) | 3 | 0 | 0 | MainWindow.Mixer.cs |
| F8 | Farbe vom Bildschirm | Notch öffnen → Pipette → Klick auf Pixel (Pfeile/Enter optional) → Hex kopiert | 3 | 0 | 0 | MainWindow.ColorPicker.cs |
| F9 | Timer starten | Rechtsklick Notch → Timer → Dauer (1–60 min) | 3 | 0 | 0 | MainWindow.Timer.cs |
| F10 | Sleep Timer | Notch öffnen (Musik läuft) → Mond → 15/30/60 min | 3 | 0 | 0 | MainWindow.Sleep.cs |
| F11 | Datei beamen (Freund) | Datei auf Notch ziehen → Freund anklicken → Fortschritt → fertig | 1 Drag + 1 | 0 | Übertragung | MainWindow.Share.cs |
| F12 | Datei per Code empfangen | Code kopieren → Rechtsklick → "Receive with copied code" → Annehmen | 3 | 0 | Übertragung | MainWindow.Share.cs |
| F13 | Screenshot weitergeben | Win+Shift+S → Pop-out → "Copy text" / "Shelf" / "Send" | 1 | 0 | OCR | MainWindow.Screenshot.cs |
| F14 | Shelf | Datei in Drop-View auf "Shelf" → später aus offener Notch herausziehen | 2 Drags | 0 | 0 | MainWindow.Shelf.cs |
| F15 | Einstellung ändern | Rechtsklick → Settings… → Seite in Sidebar → Schalter → Apply → (Esc) | 5 | 0 | 0 | SettingsWindow.cs, MainWindow.Settings.cs |
| F16 | Wetter-Ort setzen | Rechtsklick → Customize → Weather → Ort tippen → Enter | 3 | 1 | Geocoding | MainWindow.Weather.cs |
| F17 | Freund hinzufügen | Rechtsklick → Friends and chat… → Add a friend → Code kopieren/einfügen → Annehmen | 4–5 | 1 | ntfy | AddFriendPage.cs, MainWindow.Invites.cs |
| F18 | Chatten | Holm window → Chat → Freund → tippen → Enter (GIF, Reaktion optional) | 3 | 1 | 0 | ChatPage.cs |
| F19 | Wi-Fi und Speedtest | Rechtsklick → Wi-Fi and speed test… → Start → ~15 s → Wert klicken = kopiert | 3 | 0 | ~15 s | NetworkPage.cs, SpeedTest.cs |
| F20 | Update installieren | Settings → Updates → Install → Neustart (oder Klick auf Update-Pop-out) | 3 / 1 | 0 | Download | MainWindow.Updates.cs |
| F21 | Profil bearbeiten | Holm window → Profilkarte → Name/Status/Farbe/Bild → Apply | 3–5 | 2 | 0 | ProfileEditor.cs |
| F22 | Pop-outs (passiv) | Lautstärke, Kopfhörer, Caps Lock, USB (Open/Eject), Download (Open/Show), Anruf (Mute), VPN, Online/Offline, Nachricht | 0–1 | 0 | 0 | MainWindow.*.cs |
| F23 | Bildschirm wählen / beenden | Rechtsklick → Screen → Monitor / Quit | 2 | 0 | 0 | MainWindow.xaml |

## Smoke-Abdeckung (Gate)

- `--snap` (Debug, Testdaten) rendert 70 Zustände: idle, compact, peek, expanded (+ alle Tool-Seiten), Pop-outs,
  Beam, Hub-Seiten (Chat, Aktivität, Achievements, Add friend), Settings. Läuft ohne Fehler bis zum Ende = Smoke OK.
- 1020 Unit-/Integrationstests (xUnit) decken Parser (Erinnerungen, Rechner), Beam/Relay/SecureChannel, Freunde,
  Chat, Settings, Updates usw. ab.
- `--bench` (Bench-Build) öffnet/schliesst die Notch 10× (F1).
