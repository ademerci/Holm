# Persona 9 – Designer

"nicht live getestet"

Geprüft: expanded.png, expanded-today.png (F1), settings.png, settings-popouts.png (F15), hub-chat.png (F18). Code-Stichprobe: SettingsWindow.cs:169, 181-187, 230-243; ChatPage.cs:202. Nicht geprüft: Hover-Tooltips der Notch-Icons, aktivierter Apply-Zustand, Seiten unter Weather, Wetter-Ort-Dialog, Chat live.

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P1 | Holm-Fenster, Sidebar | "Active now" und "Friends" haben dasselbe Zwei-Personen-Icon. | settings.png (95,393) und (95,1312) | 3 | oft | Eindeutige Icons pro Bereich. | hoch |
| P2 | Holm-Fenster, General und Pop-outs | Schalter grün, Akzent in der Notch pink, Navigation blau: drei Akzentfarben. | settings.png (1775,372); settings-popouts.png (1775,258); expanded.png (173-345,275) | 3 | immer | Schalter in Produkt-Akzentfarbe. Grün nur als Status. | hoch |
| P3 | Notch, Musik- und Heute-Ansicht | Pink, Pfirsich, Orange, Gelb, Blau gemischt – wirkt wie fünf Themes. | expanded-today.png; expanded.png | 4 | immer | Ein Akzent pro Ansicht; Cover liefert Farbe, Sekundärtext neutral. | hoch |
| P4 | Notch, Werkzeugleiste oben | Sechs Icons ohne Beschriftung plus isolierter, blasser Mond. | expanded.png (355-770,62), Mond (1010,60) | 4 | immer | Labels bei Hover oder Mond als Teil der Leiste. | mittel |
| P5 | Notch, untere Leiste | Bluetooth- und Lautsprecher-Icon ohne Text; oranger Stern (AI-Button) ohne erkennbare Funktion; neun Elemente auf einer Zeile. | expanded.png (620-1025,470-495); settings.png (100,107) | 3 | immer | Leiste kürzen; Icons mit Text. | mittel |
| P6 | Notch, Datum | "Sun, Nov 15" vs "Sunday, November 15". | expanded.png (83,80); expanded-today.png (85,262) | 2 | immer | Ein Format überall. | hoch |
| P7 | Holm-Fenster, Sidebar | Profil doppelt: Karte oben und Eintrag "Profile". | settings.png | 2 | oft | Nur Karte behalten. | hoch |
| P8 | Holm-Fenster, Chat | "sealed so only you two can read them" steht zweimal. | hub-chat.png (1296,155), (1360,415) | 2 | oft | Einmal im Seitenkopf. | hoch |
| P9 | Holm-Fenster, Chat-Liste | Avatare gemischt (Fotos, Initialen). | hub-chat.png | 2 | oft | Einheitliche Fallback-Avatare. | hoch |
| P10 | Holm-Fenster, Chat-Eingabe und Buttons | Senden-Button dunkelblau, wenig Kontrast; Radien uneinheitlich. | hub-chat.png (1785,1318); settings.png; SettingsWindow.cs:181-183 | 2 | oft | Eine Radiusfamilie, ein Aktions-Blau. | mittel |

Noten (1-10):
- Verständlichkeit: 7
- Tempo: 6
- Vertrauen: 7
- Optik: 6
- Fehlertoleranz: 7
- Gesamt: 6

Behalten: Die Musik-Ansicht mit Cover, großem Titel, Fortschritt und dunklem Grund ist das Beste an Holm und sollte in jeder Überarbeitung unangetastet bleiben.
