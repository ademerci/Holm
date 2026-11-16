# Persona 8 – Konkurrenz-Vergleicher

nicht live getestet

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| B1 | Beam, Code-Weg | Code nur als langer Text in der Zwischenablage, kein QR-Code (Phone-Pfad hat einen). | `airdrop-code.png`; `MainWindow.Phone.cs:64` | 3 | oft | QR-Code neben dem Text. | hoch |
| B2 | Screenshot-Pop-out, Drag | Screenshot lässt sich nicht aus dem Pop-out ziehen; Thumbnail-Klick öffnet nur. | `MainWindow.Screenshot.cs:16-17`, `:206-218`, `:221-233`; `MainWindow.Shelf.cs:149` | 3 | oft | Thumbnail als Drag-Quelle. | mittel |
| B3 | Screenshot-Pop-out, Bearbeitung | Kein Bearbeiten/Löschen; nur Copy text, Shelf, Send. | `MainWindow.Screenshot.cs:128-132`; `screenshot.png` | 3 | oft | Löschen/Zuschneiden oder bewusst weglassen. | mittel |
| B4 | Screenshot-Pop-out, Verweildauer | Verschwindet nach 6 s (nach Copy text 4 s). Hover nicht geprüft. | `MainWindow.Screenshot.cs:133`, `:166` | 2 | oft | Hover pausiert Timer. | mittel |
| B5 | Screenshot-Pop-out, Copy text | Ohne OCR-Sprache fehlt Knopf/Fehlermeldung. | `MainWindow.Screenshot.cs:132`, `:151`; `screenshot-text.png` | 2 | selten | Link zu Windows-Einstellungen. | hoch |
| B6 | Beam, Empfang | Keine Vorschau, Speicherort erst danach. | `airdrop-ask.png`, `airdrop-saved.png` | 2 | selten | Vorschau bei Bildern; Speicherort vorher. | mittel |
| B7 | Beam, Phone-Pfad | Gleiches WLAN nötig, Link läuft nach 10 min ab. | `MainWindow.Phone.cs:20`, `:39-41`, `:66` | 2 | selten | Hinweis zum gemeinsamen Netz. | hoch |
| B8 | Musik, Top-Leiste | Sieben Werkzeug-Icons ohne Text, nur Tooltip. Mond unerklärt. | `expanded.png`; `MainWindow.xaml:537-553` | 2 | oft | Hover-Beschriftung. | mittel |
| B9 | Musik, AI-Knopf | Oranger AI-Knopf immer in der Musikleiste, hat mit Musik nichts zu tun. | `expanded.png`; `AiLauncher.cs:14-40`; `MainWindow.xaml.cs:1105-1108` | 2 | immer | Ins Menü oder optional. | mittel |
| B10 | Musik, Lautstärke | Kleiner Slider (84 px); grosser Pop-out nur kurz. | `expanded.png`; `MainWindow.xaml:915`; `volume.png` | 2 | oft | Slider grösser. | niedrig |

Nicht geprüft: Hover am Screenshot-Pop-out, Echtheit des Thumbnails, Tooltip AI-Knopf und Mond, Speicherort beim Empfang, Phone-Pfad live, Hover-Verzögerung-Einstellung.

Verworfen nach Prüfung: Shuffle/Repeat fehlen nicht (`MainWindow.xaml.cs:1197-1198`, `expanded-playmodes.png`); Nearby fehlt nicht (`MainWindow.Share.cs:320-321`, `:340`).

## Noten

Verständlichkeit: 7
Tempo: 8
Vertrauen: 7
Optik: 8
Fehlertoleranz: 6
Gesamt: 7

## Behalten

Die Beam-Auswahl mit Freund, Phone, Shelf und Code auf einer Fläche, die sich nach dem Ziel richtet, ist die Stärke von Holm und lässt sich nur schwer kopieren.
