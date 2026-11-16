# Analyse C – Daten, Disk, Netzwerk (Subagent, nur lesend)

| Fund | Beleg | Wirkung | Aufwand | Risiko |
|---|---|---|---|---|
| settings.json nicht atomar geschrieben (File.WriteAllText direkt); Load schluckt Fehler -> leere Defaults. Achievements macht tmp+Move. | Settings.cs:179-190 vs Achievements.cs:125-128 | Datenverlust-Schutz | S | niedrig |
| Settings.Save() synchron auf UI-Thread, >90 Aufrufstellen, kein Debounce (nur Notizen 800 ms) | Settings.cs:189, FriendsService.cs:101-709 | wenige ms pro Save | M | mittel |
| FriendsSince bei jedem ntfy-Poll-Fortschritt gespeichert | FriendsService.cs:534 | unnötige Disk-Writes | S | niedrig |
| OnLoaded startet ~15 Dienste synchron (Bluetooth, Power, VPN, Privacy-Registry, DiskSpace, USB…) | MainWindow.xaml.cs:208-249, PrivacyMonitor.cs:94-111 | Zeit bis erster Frame (nicht gemessen) | M | mittel |
| AppSettings.Load als Feldinitialisierer im Konstruktor | MainWindow.xaml.cs:48 | einige ms | S | niedrig |
| HttpClients ohne AutomaticDecompression (Weather, Focumon, UpdateService) | Weather.cs:39, MainWindow.Focumon.cs:21, UpdateService.cs:35 | kleinere Payloads | S | niedrig |
| Update-Check ohne ETag | UpdateService.cs:41-52 | Traffic, Rate-Limit | S | niedrig |
| Wetter: Sofort-Fetch bei jedem StartWeather, kein persistierter Cache | MainWindow.Weather.cs:34-45 | doppelte Requests | S | niedrig |
| Forecast-URL schon schlank | Weather.cs:49 | gering | S | niedrig |
| GIF-Cache: SetLastWriteTime pro Hit, Verzeichnis-Enumeration pro Download | Gifs.cs:381-385, 425-427 | I/O beim Picker | S | niedrig |
| GIF/Giphy JSON ohne Dekompression | Gifs.cs:281, 547 | gering | S | niedrig |
| Messages-Poll alle 2 s zusätzlich zum FileSystemWatcher | MessagesService.cs:98-105 | 30 Wakeups/min | M | mittel |
| Avatar-Decode ohne DecodePixelWidth (Bilder ≤1.5 KB) | ProfilePicture.cs:90-110 | vernachlässigbar | S | niedrig |
| PrivacyMonitor-Konstruktor Registry-Scan auf UI-Thread | PrivacyMonitor.cs:93-111 | wenige ms | S | niedrig |
| FriendInbox Reconnect-Backoff nicht geprüft | Sharing/Friends.cs:460-468, 544 | potenziell | S | niedrig |

Top 3 (Agent): atomares Settings-Schreiben + FriendsSince-Debounce; Startpfad entschlacken und messen; Save-Debounce + ETag.
