# Analyse D – Prozesse und Build (Subagent, nur lesend)

| Fund | Beleg | Wirkung | Aufwand | Risiko |
|---|---|---|---|---|
| Kein Autofocus in Reminder/Rechner/Notiz/Wetter-Box (Notch NOACTIVATE, Tastatur erst nach Klick ins Feld) | MainWindow.Tools.cs:54-62,87,97; MainWindow.Reminders.cs:138 | F2/F3/F5: je 1 Klick weniger | M | mittel (Fokusklau) |
| Wetter-Ort: Enter/Esc erst nach Klick; Fehlertext ohne nächsten Schritt | MainWindow.Weather.cs:175-185 | F16 3 -> 2 K | S | niedrig |
| Speedtest startet nicht selbst | NetworkPage.cs:56 | F19 3 -> 2 K | S | mittel (Datenverbrauch) – bleibt Opt-in |
| Unapplied-Dialog beim Schliessen: Default-Button fehlt | SettingsWindow.cs:227-241 | Enter statt Klick | S | niedrig |
| Apply-Semantik entfernen | MainWindow.Settings.cs:21 | F15 5 -> 4 K | L | hoch – Verhaltenswechsel, nicht tun |
| Profil-Editor ohne Enter-zum-Speichern | ProfileEditor.cs | F21 -1 K | S | niedrig |
| Hover-Delay 140 ms – kein Hebel | MainWindow.xaml.cs:115-116 | keine | – | – |
| _watchTimer 1.5 s immer | MainWindow.xaml.cs:124,214,358-364 | Idle-Wakeups | M | mittel |
| _btPoll 60 s + 2 Start-Scans | MainWindow.xaml.cs:125-127,215,860-866 | wenig | M | mittel |
| _liveTimer nur offen – ok | MainWindow.xaml.cs:375,421-425 | keine | – | – |
| CI: Testbefehl dreifach gepflegt | release.yml, test.yml | Wartung | S | niedrig |
| setup-dotnet ohne NuGet-Cache | test.yml, release.yml, footprint.yml | 30-60 s/Run | S | niedrig |
| footprint.yml baut doppelt (nötig) | footprint.yml | gering | M | niedrig |
| website.yml läuft bei docs/** – docs/opt löst Website-Build/Deploy aus | website.yml:4-8 | unnötige Deploys | S | niedrig |
| Fehlerpfad Settings-Apply unvollständig geprüft | SettingsWindow.cs:438-462 | unklar | S | niedrig |

Top 3 (Agent): Autofocus Tool-Felder; CI-Cache + docs-Filter; Enter/Default-Buttons.
