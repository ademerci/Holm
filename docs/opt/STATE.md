# STATE – Optimierung Holm (lies das zuerst nach Kontext-Reset)

**Aktuelle Phase:** 4, Welle 1 (Composite-Ausschlüsse uncommittet in csproj, A/B läuft; Icon im Stash)
**Branch:** `opt/lean` · **Start-Tag:** `opt/start` (780965e) · nichts pushen, nichts mergen.
Achtung: im selben Ordner existiert eine zweite (idle) Claude-Session "Holm: stability fixes". Nichts davon anfassen.

## Befehle

```powershell
pwsh -NoProfile -File docs/opt/gate.ps1                       # Gate. Exit 0 = grün
pwsh -NoProfile -File docs/opt/measure.ps1 -Out docs/opt/measure-after.json   # 3 Läufe, Median
pwsh -NoProfile -File docs/opt/ab-start.ps1 -A <exe> -B <exe> -Pairs 15      # A/B Start, abwechselnd
# Bench-Exe bauen (Release + Bench-Schalter):
dotnet publish WinNotch -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:PublishReadyToRun=true `
  -p:DebugType=none -p:IncludeNativeLibrariesForSelfExtract=true -p:HolmBench=true -o out/x-bench
# SDK: $env:LOCALAPPDATA\Microsoft\dotnet-sdk10 (Skripte setzen PATH selbst)
```

## Fallen (gelernt)

- **obj-Falle:** zwei Publishes mit verschiedenen R2R-Einstellungen hintereinander mischen Zwischenstände → Exe stürzt
  mit 0xC0000602 ab. Vor jeder Variante `WinNotch/obj` und `WinNotch/bin` löschen.
- **Rauschen:** Einzelmessungen streuen ±50 % (Nutzer am PC, Musik). Nur abwechselnde A/B-Läufe zählen.
- **PowerShell:** Variablen sind case-insensitiv (`$runs` = `$Runs`).
- **Trace:** Zeit pro Methode in OnLoaded ist unzuverlässig (JIT/Typladen/Lock-Konkurrenz mit Hintergrund-Threads).
- **Doppel-Notch:** Test-Instanz und eigenes Holm verdecken sich → WatchFullscreen-Kosten sind Artefakt.

## Erledigt

- Phase 0–3. Commits: 2a92abd (HOLM_DATA), 6cbf7c3 (Mess-Infrastruktur + Ready-Stempel).
- Composite aus: verworfen (ready +703 ms, CPU +1062 ms, JIT ×2). Composite-Roots: 3× gescheitert (SDK-Bug).
- Composite-Ausschlüsse (44 Assemblies): 213,15 → 204,38 MB, JIT bis ready gleich.

## Offen / flüchtig

- `git stash@{0}`: Icon-Änderung (SettingsWindow.cs + csproj). Erst nach Ausschluss-Commit poppen; brand.png mit 128 px neu.
- `out/ecoqos.patch`: EcoQoS erst nach Start (Hypothese, A/B nötig).
- Hub-Test 5 vs 20 Runden (HOLM_BENCH_HUB).
- Gate einmal grün auf 6cbf7c3 (vor Ausschluss-Commit).
- GOALS/BASELINE neu ableiten (Composite-Variante verworfen) → DECISIONS.

## Messwerte (Baseline)

Exe 212,96 MB · Zip 82,04 MB · bedienbar 2947 ms (A/B-Median, JIT 633) · CPU 11 s 6844 ms · Private 142 MB.
