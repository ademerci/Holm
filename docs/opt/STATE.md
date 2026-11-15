# STATE – Optimierung Holm (lies das zuerst nach Kontext-Reset)

**Aktuelle Phase:** 2 (Baseline läuft) / 3 (Analyse-Agents laufen)
**Branch:** `opt/lean` · **Start-Tag:** `opt/start` (780965e) · nichts pushen, nichts mergen.

## Befehle

```powershell
# Gate (Build -warnaserror, 1020 Tests, Smoke --snap 70 Bilder). Exit 0 = grün.
pwsh -NoProfile -File docs/opt/gate.ps1
# Messung (3 Läufe, Median) -> JSON
pwsh -NoProfile -File docs/opt/measure.ps1 -Out docs/opt/measure-after.json
# .NET 10 SDK liegt user-lokal: $env:LOCALAPPDATA\Microsoft\dotnet-sdk10 (Skripte setzen PATH selbst)
# Test-Instanz neben eigenem Holm: $env:HOLM_DATA = <Ordner>  (nur Debug/Bench-Build)
```

Testdaten: `out/opt-data/settings.seed.json` (offline-Konfiguration, siehe DECISIONS.md 5). `out/` ist gitignored.

## Erledigt

- Phase 0: Stack erkannt (WPF .NET 10, xUnit, keine Lint-Config, Website out of scope). Branch + Tag. Dev-Schalter
  `HOLM_DATA` (Commit 2a92abd). App startet mit Testdaten. FLOWS.md (23 Abläufe).
- Phase 1: 1020/1020 Tests grün (vorher). `--snap` rendert 70 Zustände. gate.ps1 geschrieben.

## Offen

- Phase 1: Screenshots nach docs/opt/before/, Gate einmal grün laufen lassen.
- Phase 2: BASELINE.md, GOALS.md, Advisor.
- Phase 3–10.

## Messwerte

siehe BASELINE.md (nach Abschluss der Messung).
