# Persona 3 – Unterwegs-Nutzer

nicht live getestet. Geprüft wurden Code (`WinNotch\`) und Screenshots (`docs\opt\before\`).

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | Beamen an Freund über Internet | Kommt die Relay-Verbindung nicht in 15 s zustande: "X is offline / It goes once their Holm is back". Datei wandert still in die Warteschlange, obwohl das eigene Hotel-WLAN schuld ist. | `MainWindow.Share.cs:487`, `:496-512`; `FriendsService.cs:28`; `RelayLink.cs:72-85`; `Settings.cs:119`; `Friends.cs:538-570` | 5 | oft | Bei Timeout "Verbindung zu schwach / nochmal versuchen" statt "offline"; Retry-Knopf; Warteschlange nicht still. | hoch |
| P2 | Beamen, Übertragung hängt | Fortschritt bleibt stehen; kein Timeout, keine Geschwindigkeit, keine Restzeit; nur Cancel. | `FileTransfer.cs:84-96`; `RelayLink.cs:35`; `MainWindow.Share.cs:870`; `airdrop-progress.png` | 4 | oft | Warnung nach 30 s ohne Bytes, Retry; Geschwindigkeit/Restzeit; Heartbeat. | mittel |
| P3 | Speedtest, Ping-Phase | "Measuring ping…" kann unbegrenzt hängen; kein Timeout pro Ping. UI verspricht "about 15 seconds". | `SpeedTest.cs:49`, `:75-84`, `:45`; `NetworkPage.cs:90`, `:183` | 4 | oft | CancelAfter ~5 s pro Ping, Gesamtlimit, klare Fehlermeldung. | hoch |
| P4 | Fehlerende bei Beamen und Speedtest | Fehler verschwindet nach 5 s; Meldungen technisch (Hostname, "The relay said: …", rohe IO-Texte); kein Retry. | `MainWindow.Share.cs:873-880`, `:895`; `Friends.cs:487`; `RelayLink.cs:44`; `FileTransfer.cs:213-216` | 3 | oft | Retry-Knopf; Fehler stehen lassen; Klartext. | hoch |
| P5 | Speedtest, Fehler oder Stop | Gemessene Ping/Download-Werte verschwinden bei Fehler/Stop. | `NetworkPage.cs:149-150`, `:154`, `:159`, `:164` | 3 | oft | Teilwerte zwischenspeichern und zeigen. | hoch |
| P6 | Speedtest auf metered Netz | Hinweis "~600 MB" klein (11 px, Faint); keine Bestätigung, keine Metered-Prüfung. | `NetworkPage.cs:189`, `:28`; `SpeedTest.cs:40`, `:212` | 3 | selten | Bei metered Verbindung vor Start bestätigen. | hoch |
| P7 | F1 Notch, Touchpad | Seek-/Lautstärkeleiste 16 px hoch, sichtbar 4 px; Collapse 300 ms nach Verlassen. | `Controls.cs:77`, `:84`; `MainWindow.xaml.cs:115-119`; `expanded.png`; `MainWindow.xaml:635-638` | 2 | oft | Trefferfläche ~24 px, Balken dicker. | mittel |
| P8 | F11 Beamen, Einstieg | Nur Drag auf die Notch; kein "Send file…" im Rechtsklick-Menü. | `MainWindow.Share.cs:19`; `MainWindow.Settings.cs:57`; `ChatPage.cs:197-198`; `MainWindow.xaml:239-245` | 2 | oft | "Send file…" im Menü mit Dateidialog. | mittel |
| P9 | F1 Prev/Next | Kein ToolTip; keine sofortige Rückmeldung. | `MainWindow.xaml:632`, `:638`, `:629`, `:642`; `MainWindow.xaml.cs:1372`, `:1376-1377` | 1 | oft | ToolTip "Previous"/"Next". | mittel |

**Noten (1-10)**

Verständlichkeit: 6
Tempo: 8
Vertrauen: 3
Optik: 8
Fehlertoleranz: 3
Gesamt: 5

**Behalten:** Die Musiksteuerung in der Notch mit großen Tasten und sofortigem Play-Feedback, weil sie unterwegs am schnellsten zum Ziel führt.
