# GOALS – ein Ziel pro Messwert

| Messwert | Vorher | Ziel | Begründung |
|---|---|---|---|
| Holm.exe | 212,96 MB | **≤ 165 MB (−22 %)** | Composite-R2R kompiliert bereits vorkompilierte Framework-Assemblies neu (122 MB). Ohne Composite gemessen 156,75 MB. |
| Release-Zip | 82,04 MB | ≤ 66 MB | folgt aus Exe (gemessen 64,23 MB) |
| Direkte Abhängigkeiten | 2 | ≤ 2 | Regel 8. QRCoder-Ersatz spart < 1 MB, Emoji.Wpf ist Funktion → beide bleiben. |
| Assets | 385,1 KB | ≤ 20 KB im Exe als WPF-Resource | notch.ico liegt doppelt (App-Icon + Resource für 30-px-Bild). |
| Quellcode-Zeilen | 26 428 | ≤ 26 428 | Dead-Code-Scan fand nichts; nur nicht wachsen lassen. |
| Build / Publish / Tests | 3,55 s / 67,2 s / 52,2 s | nicht schlechter als +10 % | Publish ohne Composite sollte schneller werden; kein eigenes Ziel. |
| Start bis Fenster (A/B) | 2188 ms | nicht schlechter als +5 % | Rauschen ±30 %; Startpfad-Umbau nur mit A/B-Beleg. |
| CPU erste 10 s (A/B) | 6375 ms | ≤ 6375 ms | einmal pro Anmeldung; darf nicht steigen (siehe Advisor-Frage). |
| Private Memory idle | 149–154 MB | ≤ 135 MB | ohne Composite gemessen 129,7 MB (A/B 132,1). |
| Peak Working Set | 241 MB | ≤ 225 MB | ohne Composite gemessen 221,5 MB. |
| Holm window öffnen | +56 MB bleibt | ≤ +40 MB | nur wenn Ursache messbar findbar; sonst Backlog. |
| Idle-CPU | 1583–3188 ms/min | nicht schlechter | kein stabiler Messwert (Musik); Profil zeigt keinen vermeidbaren Posten > 1 %. |
| Bench öffnen/schliessen | 653–822 ms CPU/Runde | nicht schlechter | bereits stark optimiert (csproj, Motion). |
| F2 Notiz / F3 Erinnerung / F5 Rechnen | 3 / 3 / 4 Klicks | 2 / 2 / 3 | Tool-Seite per Klick geöffnet = Absicht zu tippen → Feld bekommt Fokus. |
| F16 Wetter-Ort | 4 Klicks | 3 | gleicher Mechanismus |
| F21 Profil | kein Enter | Enter = Apply | Tastaturbedienung |
| UX-Noten Roast | – | Runde 2 ≥ Runde 1 + 1,0 | Auftrag |
