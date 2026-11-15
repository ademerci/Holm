# Persona 7 – Skeptiker

"nicht live getestet"

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | Beam, Annahme-Dialog | "Lena Hoffmann wants to send you" mit Avatar, als wäre der Name geprüft. Der Name stammt vom Absender selbst. Im Netz kein Gegencheck. | `airdrop-ask.png`; `WinNotch\Sharing\FileTransfer.cs:237`; README Z. 147 ("the notches don't verify each other") | 4 | oft | Netz-Absender als "unbestätigt" kennzeichnen; Satz "Nur annehmen, wenn du diese Person kennst." | hoch |
| P2 | Freunde hinzufügen, Einladungscode | "nobody has to accept anything", "works for everyone you give it to until tomorrow". Wer den Code hat, wird Freund und sieht Aktivität. Steht nicht im Dialog. | `hub-add.png`; `AddFriendPage.cs:70`, `:160`; `Sharing\FriendInvite.cs:13`; `MainWindow.Settings.cs:63` | 4 | oft | "Jeder mit diesem Code wird dein Freund und sieht deine Aktivität." + Widerruf. | hoch |
| P3 | Freunde hinzufügen, Datenweg | Einladung über öffentlichen Server ntfy.sh (sieht IP und Zeit). Im Dialog nichts. | `Sharing\Friends.cs:113`; `FriendInvite.cs:78`; README Z. 146 | 3 | oft | Zeile: "Läuft über ntfy.sh. Der Server sieht IP-Adressen und Zeiten, nicht die Inhalte." | hoch |
| P4 | Beam, Senden-Popup | "Code" geht über öffentlichen Magic-Wormhole-Relay; Popup sagt nichts. | `airdrop-share.png`; README Z. 148 | 3 | oft | Untertitel bei "Code": öffentlicher Vermittlungsserver, verschlüsselt. | hoch |
| P5 | Wetter, Schalter "Set up weather using approximate location" | Eintrag in Settings ohne Beschreibung (""). Erklärung (einmalig, gerundet, Open-Meteo) nur im Tooltip des Rechtsklick-Menüs. | `MainWindow.Settings.cs:67-70`; `MainWindow.xaml:261`; `MainWindow.Weather.cs:228` | 4 | selten | Tooltip-Zeile als Beschreibung in die Settings, Open-Meteo nennen. | hoch |
| P6 | Wetter-Ortung, Widerspruch | Tooltip "no IP lookup service", Code setzt `AllowFallbackToConsentlessPositions()`. API-Verhalten nicht geprüft. | `WeatherLocation.cs:14`; `MainWindow.xaml:261` | 3 | selten | Fallback entfernen oder Tooltip präzisieren. | niedrig |
| P7 | Settings, "Calls and privacy" | Untertitel nennt "your clipboard", Seite hat nur Passwort-Schalter. Dass die Historie nur im Speicher liegt, steht nicht hier. | `MainWindow.Settings.cs:47`, `:53`, `:32`; README Z. 33 | 3 | oft | Seite nennt das Lesen der Zwischenablage; Historie-Option hierher. | hoch |
| P8 | Freunde hinzufügen, Fehlertexte | "The server couldn't be reached. Try again in a moment." – welcher Server? | `AddFriendPage.cs:152` | 2 | selten | Servernamen nennen; abgelaufene Codes eigens benennen. | mittel |
| P9 | Beam, "Send later" | Wo und wie lange wartende Dateien liegen, nicht geprüft. Beschreibung nur "wait and go once they're back". | `MainWindow.Settings.cs:59` | 2 | selten | Speicherort und Dauer nennen. | niedrig |

**Noten (1-10)**

- Verständlichkeit: 6
- Tempo: 7
- Vertrauen: 4
- Optik: 8
- Fehlertoleranz: 5
- Gesamt: 5

**Behalten:** Die README-Erklärung zu ntfy, Relay und Schlüsseln ist präzise und ehrlich. Sie muss nur in die Dialoge wandern, dort wo gesendet, geteilt und gelesen wird.

Nicht geprüft: AddByCode und Fehlertexte bei abgelaufenen Codes, Widerruf von Einladungen, Relay-Verhalten im Betrieb, `AllowFallbackToConsentlessPositions` in der Praxis.
