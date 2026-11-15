# Persona 7 – Skeptiker (Runde 2)

**nicht live getestet**

| ID | Ort | Was passiert | Beleg | Schwere | Wie oft | Vorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | F17 "New code" | Widerruft alten Code nicht (gilt bis Ablauf); Tooltip suggeriert Kontrolle. | `AddFriendPage.cs:58`; `Sharing\FriendInvite.cs:23`; `FriendsService.cs:118, 128-141` | 5 | selten | "Revoke all codes" oder ehrlicher Tooltip. | hoch |
| P2 | F11 "Phone" | Reines HTTP im LAN, Token im Pfad; keine Warnung zu öffentlichem WLAN. | `Sharing\PhoneShare.cs:62`; `MainWindow.Phone.cs:66` | 4 | selten | Warnung; HTTPS prüfen. | hoch |
| P3 | Settings/README Clipboard | "in memory only", aber Bilder als PNG in `%TEMP%\Holm\Clipboard`. | `MainWindow.Clipboard.cs:24, 116-122, 31, 180-189`; `MainWindow.Settings.cs:32, 53`; `README.md:33` | 4 | selten | Text korrigieren; Bilder beim Beenden löschen. | hoch |
| P4 | Beam liest Zwischenablage | Sucht Codes in jedem kopierten Text; nicht in der UI erklärt. Passwörter ausgeschlossen. | `MainWindow.Share.cs:708-716, 727`; `MainWindow.Settings.cs:57`; `Settings.cs:95` | 4 | immer | Satz im Beam-Schalter. | hoch |
| P5 | Eigene Code-Kopien | Codes/Phone-Link landen im eigenen Clipboard-Verlauf. | `MainWindow.Share.cs:1050-1058`; `AddFriendPage.cs:170`; `MainWindow.Clipboard.cs:56-86`; `Settings.cs:69` | 4 | oft | Mit Ausschlussformat markieren. | mittel |
| P6 | F17 Invite-Code | Empfänger wird ohne Rückfrage Freund, sieht Aktivität; Warnung klein. | `AddFriendPage.cs:70, 160`; `FriendsService.cs:150-166`; `Settings.cs:103` | 3 | selten | Notiz beim Annehmen. | hoch |
| P7 | F17 Server | "ntfy" ohne Erklärung, dass ntfy.sh IP und Zeit sieht. | `AddFriendPage.cs:152`; `Sharing\Friends.cs:18, 113` | 3 | immer | Hinweis unter dem Code. | hoch |
| P8 | F11 Nearby | Name per Broadcast, Gegenstelle nicht verifiziert. | `Sharing\NearbyService.cs:20-23`; `MainWindow.Share.cs:799` | 3 | oft | "Unknown device". | hoch |
| P9 | Wetter-Standard | Auto-Ortung standardmässig an; Rundung nicht beziffert. | `Settings.cs:91`; `MainWindow.Weather.cs:38-51`; `WeatherLocation.cs:23`; `MainWindow.Settings.cs:69` | 3 | selten | "about 10 km". | mittel |
| P10 | Datenschutz verteilt | Schalter auf vier Seiten. | `MainWindow.Settings.cs:47-70` | 2 | selten | Übersicht. | hoch |
| P11 | Kachel "Code" | Relay sieht IPs, nicht erklärt. | `Sharing\RelayLink.cs:19`; `after/airdrop-share.png` | 2 | oft | Untertitel. | hoch |

(Hinweis Triage: 11 statt max. 10 Punkte.)

## Noten (1-10)

- Verständlichkeit: 6
- Tempo: nicht geprüft
- Vertrauen: 4
- Optik: 8
- Fehlertoleranz: 6
- Gesamt: 5

## Behalten

Die Passwort-Sperre (Format wird vor dem Lesen geprüft) und die Annahme mit Rückfrage bei Dateien sind genau richtig und sollten so bleiben.
