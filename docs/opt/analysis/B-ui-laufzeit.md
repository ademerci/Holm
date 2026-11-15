# Analyse B – UI-Laufzeit (Subagent, nur lesend)

| Fund | Beleg | Wirkung | Aufwand | Risiko |
|---|---|---|---|---|
| _watchTimer 1.5 s dauerhaft (Vollbild-/Foreground-/Cover-Prüfung, OpenProcess) | MainWindow.xaml.cs:124,214,358-392 | einziger Dauer-Wakeup-Takt | M | mittel |
| _btPoll 60 s + 3 Scans beim Start (OnLoaded, _btSoon, _btLate) | MainWindow.xaml.cs:125-127,212,215; MainWindow.Bluetooth.cs:124-127 | niedrig-mittel | S | niedrig |
| MessagesService._poll 2 s zusätzlich zu FileSystemWatcher | MessagesService.cs:68,103-105 | 0.5 Wakeups/s | S | mittel |
| _clickAway 35 ms solange offen | MainWindow.ClickAway.cs:28,36-45 | nur offen | M | mittel |
| _eqTimer 40 ms (25 fps) bei Musik + AudioMeter-Thread | MainWindow.xaml.cs:741-775; AudioMeter.cs | mittel-hoch bei Musik | S | niedrig |
| _focumonTick + _liveTimer beide 500 ms, ReadVolume-Poll redundant | MainWindow.xaml.cs:123,421,1232; MainWindow.Focumon.cs:93-125 | nur offen | S | niedrig |
| Settings.Save() synchron, 77 Aufrufstellen, kein Debounce | Settings.cs:183-192 | mittel | S | mittel |
| ChatPage.ShowMessages baut alle Bubbles neu, nicht virtualisiert | ChatPage.cs:292-304,455-480 | lange Chats | M | mittel |
| OnLoaded startet ~20 Dienste synchron | MainWindow.xaml.cs:131-150,208-250 | Startzeit | M | mittel |
| Feld-Initialisierer eager (Settings, VolumeService, AudioMeter, Slider) | MainWindow.xaml.cs:48-72 | niedrig | S | niedrig |
| PrivacyMonitor _verify 5 s mit Process.GetProcessesByName bei aktivem Mikro | PrivacyMonitor.cs:87,107,200,289-298 | nur bei Calls | M | mittel |
| Notch-Resize animiert Width/Height + Shape-Rebuild pro Frame | MainWindow.xaml.cs:509-514 | Übergänge | L | hoch |
| ActivityPage/ChatPage Lambdas – Detach korrekt, kein echtes Leck | ActivityPage.cs:29-41 | niedrig | S | niedrig |
| ChatPage Clip-Geometrie pro SizeChanged neu | ChatPage.cs:250-251 | niedrig | S | niedrig |
| Clipboard-Thumbs evtl. ohne DecodePixelWidth (bis 20 Bilder voll dekodiert) | MainWindow.Clipboard.cs:150-151; ClipboardHistory.cs:20,29 | Speicher bei Bild-Kopien | S | niedrig |

Top 3 (Agent): Save debouncen; Dauer-Wakeups (_watchTimer, _btPoll, Messages-Poll) entschärfen; OnLoaded entzerren.
