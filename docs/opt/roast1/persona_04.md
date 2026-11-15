# Persona 4 – Screenreader und Tastatur

"nicht live getestet"

Kurz: Holm ist für Tastatur- und Screenreader-Nutzer praktisch unbenutzbar. Der Code enthält kein einziges `AutomationProperties` (0 Treffer in .cs/.xaml). Die Notch lässt sich per Tastatur nicht öffnen, und die Schalter in Holm sind nur per Maus bedienbar. Nur Enter im Chat und die Dialoge funktionieren.

(Hinweis Triage: Bericht hat 16 statt max. 10 Punkte; alle aufgenommen, gewichtet wie die anderen.)

| ID | Ort | Was passiert | Beleg | Schwere (1-5) | Wie oft | Verbesserungsvorschlag | Sicherheit |
|---|---|---|---|---|---|---|---|
| P4-01 | Holm-Fenster, Einstellungsschalter (F15) | Zeilen mit Schaltern sind `Grid` ohne Fokus und ohne Tasten. Schalter per Tastatur nicht umlegbar; `ToggleSwitch : FrameworkElement`, nur gezeichnet. | `SettingsWindow.cs:352-381`, `:469`; `settings.png` | 5 | immer | Zeile `Focusable`, Space/Enter schaltet; `AutomationProperties.Name` + Toggle-Pattern. | hoch |
| P4-02 | Notch, Einstieg Einstellungen (F15) | "Settings…" nur im Rechtsklick-Menü. Kein Tray-Icon, kein Hotkey. | `MainWindow.xaml:245`, `:231`; kein NotifyIcon/RegisterHotKey | 5 | immer | Tray-Icon oder Tastenkombination. | hoch |
| P4-03 | Notch, Öffnen per Tastatur (alle) | `WS_EX_NOACTIVATE`, Fokus nur nach Mausklick in Textfeld. Ohne Maus nicht öffnbar. | `MainWindow.xaml.cs:197`, `MainWindow.Tools.cs:102-127` | 5 | immer | Notch per Hotkey öffnen, Fokus auf erste Schaltfläche. | hoch |
| P4-04 | Holm-Fenster, Seitennavigation | Navigationspunkte sind `Border` mit Mausklick; Tab erreicht sie nicht. | `SettingsWindow.cs:308-336`; `ChatPage.cs:414-419` | 4 | oft | Als Button/ListBoxItem mit Pfeiltasten und Name. | hoch |
| P4-05 | Notch-Werkzeugleiste, Glocke (F3) | `NotchButton`-Stil `Focusable=False`, nur Glyph, Name fehlt, nur ToolTip. | `MainWindow.xaml:31-32`, `:175`, `:540-541`; `expanded-reminders.png` | 5 | immer | Focusable, `AutomationProperties.Name`, Fokus-Rahmen. | hoch |
| P4-06 | Erinnerungsliste, "×" | Nicht fokussierbar, kein Name, nur ToolTip "Remove". | `MainWindow.Reminders.cs:117-118` | 4 | oft | Fokus erlauben, Name "Remove reminder: <Text>". | hoch |
| P4-07 | Erinnerungsfeld, Fokus nach Öffnen (F3) | `ShowRemindersPage` setzt keinen Fokus; nur über Mausklick (`AllowTyping`). | `MainWindow.Reminders.cs:138-142`, `:26` | 3 | immer | `ReminderBox.Focus()` beim Öffnen. | mittel |
| P4-08 | Erinnerungsfeld, Fehler (F3) | Unlesbarer Text: `return` ohne Meldung; ohne LiveSetting keine Ansage. Platzhalter mit Beispielen hilft. | `MainWindow.Reminders.cs:89-97`, `:77-87`; `MainWindow.xaml:835` | 3 | oft | Fehler per `LiveSetting="Assertive"` ankündigen; Feld benennen. | mittel |
| P4-09 | Chat-Eingabe (F18) | Enter sendet, Shift+Enter neue Zeile. Senden-Knopf nur Maus. Stärkster Punkt. | `ChatPage.cs:221-227`, `:235-237`; `hub-chat.png` | 2 | immer | Senden-Knopf fokussierbar, Name "Send". | hoch |
| P4-10 | Chat, Fokus beim Öffnen | Eingabefeld bekommt Fokus. Gut. | `MainWindow.Hub.cs:32`; `SettingsWindow.cs:222-223`; `ChatPage.cs:260`, `:313` | 1 | immer | Gut lassen. | hoch |
| P4-11 | Chat, Freundesliste | Freunde nur per Mausklick wählbar. | `ChatPage.cs:414-419`; `hub-chat.png` | 4 | oft | ListBox mit Pfeiltasten, Name pro Zeile. | hoch |
| P4-12 | Chat, eingehende Nachrichten | Keine Ansage (kein LiveSetting im Code). | `ChatPage.cs:296-305` | 3 | oft | LiveSetting="Polite". | mittel |
| P4-13 | Chat, erneut senden | "Not sent · Click to try again" nur Mausziel. | `ChatPage.cs:686-689` | 3 | selten | Als Button "Try sending again". | hoch |
| P4-14 | Holm-Fenster, Schließen | `Border`, nicht fokussierbar; Esc funktioniert aber. | `SettingsWindow.cs:166-171`, `:228-233` | 2 | selten | Button mit Name "Close". | hoch |
| P4-15 | Dialoge | Fokus in Dialog, Rückgabe, Esc. Sauber. | `SettingsWindow.cs:393-428` | 1 | selten | Gut lassen. | hoch |
| P4-16 | Fokus-Sichtbarkeit | `HubTheme.Action` zeigt Fokus; Notch-Stile haben keinen Fokus-Stil. | `HubTheme.cs:98-100`; `MainWindow.xaml:31-60`, `:105-132`, `:182-200` | 2 | immer | Fokus-Trigger in Notch-Stile. | mittel |

**Noten (1-10):**
- Verständlichkeit: 6
- Tempo: 2
- Vertrauen: 3
- Optik: 7
- Fehlertoleranz: 4
- Gesamt: 3

**Nicht geprüft:** echte Ansagen von Narrator/NVDA, Alt+Tab-Einstieg, Tab-Reihenfolge zur Laufzeit, Fokus-Zustände im Bild.

**Behalten:** Enter sendet im Chat und fügt Erinnerungen hinzu, mit Beispielen im Platzhalter, und Esc schließt das Holm-Fenster und seine Dialoge samt Fokusrückgabe – genau so lassen.
