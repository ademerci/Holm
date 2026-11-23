using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Holm.App.Controls;
using WinNotch;
using Design = WinNotch.Design;

namespace Holm.App;

/// <summary>
/// Settings › Design: pick one of the designs (or one you imported from a JSON file), and the accent colour. Both are
/// read once at start, so the page offers to restart Holm.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly (string Name, uint Color)[] AccentChoices =
    {
        ("Blue", 0xFF0A84FF), ("Purple", 0xFFBF5AF2), ("Pink", 0xFFFF375F), ("Red", 0xFFFF453A),
        ("Orange", 0xFFFF9F0A), ("Yellow", 0xFFFFD60A), ("Green", 0xFF30D158), ("Graphite", 0xFF8E8E93),
    };

    /// <summary>What to tell an AI so it writes a design Holm can import.</summary>
    static readonly string DesignPrompt = $"""
        Write a design for Holm, a notch app for Windows and Linux, as one JSON object. Holm imports it in Settings › Design.
        Only "name" is required. Colours are "#RRGGBB" or "#AARRGGBB" (alpha first). Keys:
        - "name": up to 40 characters; "description": one short line
        - "base": the built-in design to start from: {string.Join(", ", Designs.BuiltIn.Select(d => d.Id))}
        - "layout": "classic" (icons, round) or "instrument" (tools as words, small corners)
        - "fill": the notch's colour, or a list of 2-8 colours for a gradient; "fillKind": "vertical", "diagonal" or "conic"
        - "edge": a line around the notch and window; "edgeWidth": 0-4; "glow": true to make the edge glow
        - "ink": text and icons; "accent": buttons, switches, progress; "toggle": switches when on, if not the accent
        - "panel", "sidebar", "card", "line", "dim" (secondary text), "faint": the Holm window
        - "corners": 0-30; "glass": true for a frosted window; "shadow": a hard shadow colour
        - "font": "sans", "mono", "serif", "plex" (IBM Plex Mono) or "spectral" (Spectral serif); "caps": true for headings in capitals
        - "coverColors": true to wash the notch in the playing song's cover colours
        Keep "ink" readable on "fill" and "panel". Answer with the JSON only.
        The design I want:
        """;

    sealed class DesignView : HubView { }

    HubView DesignPage()
    {
        var content = new StackPanel { Spacing = 8 };
        var restart = new Border
        {
            IsVisible = false, Background = HubTheme.Card, CornerRadius = new CornerRadius(HubTheme.Round(12)), Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var restartRow = new DockPanel();
        var restartButton = HubTheme.Action("Restart now", primary: true);
        restartButton.Click += (_, _) => RestartHolm();
        DockPanel.SetDock(restartButton, Dock.Right);
        restartRow.Children.Add(restartButton);
        restartRow.Children.Add(new TextBlock { Text = "Holm shows the new look after a restart.", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        restart.Child = restartRow;
        content.Children.Add(restart);
        void Changed() { _settings.Save(); restart.IsVisible = true; }

        // ── designs ──
        content.Children.Add(HubTheme.Caption("Design"));
        var tiles = new WrapPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(tiles);
        string chosen = Look.Current.Id;
        void FillTiles()
        {
            tiles.Children.Clear();
            foreach (var design in Designs.BuiltIn.Concat(Designs.LoadImported()))
            {
                var tile = DesignTile(design, design.Id == chosen);
                tile.PointerReleased += (_, _) =>
                {
                    chosen = design.Id;
                    _settings.Design = design.Id;
                    Changed();
                    FillTiles();
                };
                tiles.Children.Add(tile);
            }
        }
        FillTiles();

        // ── accent ──
        content.Children.Add(new Border { Height = 10 });
        content.Children.Add(HubTheme.Caption("Accent colour"));
        var dots = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var hex = HubTheme.Field("#RRGGBB");
        hex.Width = 110;
        hex.Text = _settings.AccentColor;
        void SetAccent(string value)
        {
            _settings.AccentColor = value;
            hex.Text = value;
            Changed();
            FillDots();
        }
        void FillDots()
        {
            dots.Children.Clear();
            var own = HubTheme.Action("The design's own");
            own.Margin = new Thickness(0, 0, 12, 0);
            own.Click += (_, _) => SetAccent("");
            dots.Children.Add(own);
            foreach (var (name, color) in AccentChoices)
            {
                string value = "#" + (color & 0xFFFFFF).ToString("X6");
                bool on = string.Equals(_settings.AccentColor, value, StringComparison.OrdinalIgnoreCase);
                var dot = new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Look.Brush(color), Margin = new Thickness(0, 0, 10, 0),
                    BorderBrush = on ? Look.Ink : Brushes.Transparent, BorderThickness = new Thickness(2), Cursor = new Cursor(StandardCursorType.Hand),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                ToolTip.SetTip(dot, name);
                dot.PointerReleased += (_, _) => SetAccent(value);
                dots.Children.Add(dot);
            }
        }
        FillDots();
        var accentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) TakeHex(); };
        hex.LostFocus += (_, _) => TakeHex();
        void TakeHex()
        {
            string text = (hex.Text ?? "").Trim();
            if (text.Length > 0 && !text.StartsWith('#')) text = "#" + text;
            if (text == _settings.AccentColor) return;
            if (text.Length == 0 || text.Length == 7 && Designs.TryColor(text, out _)) SetAccent(text.ToUpperInvariant());
            else hex.Text = _settings.AccentColor; // not a colour: keep what was there
        }
        var pipette = HubTheme.Action("Pick from screen");
        pipette.Click += async (_, _) =>
        {
            var (picked, _) = await PickFromScreenAsync();
            if (picked != null && Designs.TryColor(picked, out uint c)) SetAccent("#" + (c & 0xFFFFFF).ToString("X6"));
        };
        accentRow.Children.Add(new TextBlock { Text = "Your own:", Foreground = HubTheme.Dim, VerticalAlignment = VerticalAlignment.Center });
        accentRow.Children.Add(hex);
        accentRow.Children.Add(pipette);
        content.Children.Add(dots);
        content.Children.Add(accentRow);

        // ── your own designs ──
        content.Children.Add(new Border { Height = 10 });
        content.Children.Add(HubTheme.Caption("Your own designs"));
        content.Children.Add(new TextBlock
        {
            Text = "A design is a small JSON file. Copy the prompt, add what you'd like, and let an AI write it; then import the file.",
            Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap,
        });
        var status = new TextBlock { Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var importRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
        var copyPrompt = HubTheme.Action("Copy AI prompt");
        copyPrompt.Click += async (_, _) =>
        {
            bool copied = await TrySetClipboardAsync(DesignPrompt);
            (status.Text, status.IsVisible) = (copied ? "Copied. Paste it into your AI and describe the look you want." : "The clipboard is busy; try again.", true);
        };
        var import = HubTheme.Action("Import a design…", primary: true);
        import.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(import)?.StorageProvider is not { } storage) return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import a Holm design", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Holm design") { Patterns = new[] { "*.json" } } },
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            var (design, error) = Designs.Import(path);
            if (design == null) { (status.Text, status.IsVisible) = (error ?? "That file isn't a design.", true); return; }
            (status.Text, status.IsVisible) = ($"“{design.Name}” is imported and chosen.", true);
            chosen = design.Id;
            _settings.Design = design.Id;
            Changed();
            FillTiles();
        };
        importRow.Children.Add(import);
        importRow.Children.Add(copyPrompt);
        content.Children.Add(importRow);
        content.Children.Add(status);

        return new DesignView { Child = HubTheme.Scroller(content) };
    }

    /// <summary>A design as a small card in its own colours: a bit of its window with its notch on top.</summary>
    static Border DesignTile(Design design, bool chosen)
    {
        var notch = new Border
        {
            Width = 64, Height = 16, Background = Look.NotchFill(design), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(0, 0, design.Instrument ? 3 : 8, design.Instrument ? 3 : 8),
            BorderBrush = design.Edge != 0 ? Look.Brush(design.Edge) : null, BorderThickness = new Thickness(design.Edge != 0 ? Math.Min(2, design.EdgeWidth) : 0),
        };
        if (design.Id == PrismGlass.DesignId)
            notch.Child = new PrismGlass(7) { IsHitTestVisible = false };
        var ink = Look.Brush(design.Ink);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 9) };
        words.Children.Add(new TextBlock
        {
            Text = design.Caps ? design.Name.ToUpperInvariant() : design.Name, Foreground = ink, FontFamily = Look.FontFor(design), FontSize = 13, FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var accent = new Border { Width = 18, Height = 4, CornerRadius = new CornerRadius(2), Background = Look.Brush(design.Accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
        words.Children.Add(accent);
        var face = new Grid();
        DesignDetail.AddTo(face, design, preview: true);
        if (design.Id == DotMatrix.DesignId) face.Children.Add(new Avalonia.Controls.Shapes.Rectangle { Fill = DotMatrix.Grid(design.Ink, 0x30, 5, 1.2), IsHitTestVisible = false });
        face.Children.Add(notch);
        face.Children.Add(words);
        var tile = new Border
        {
            Width = 132, Height = 84, Margin = new Thickness(0, 0, 10, 10), ClipToBounds = true,
            Background = Look.Brush(design.Panel | 0xFF000000), CornerRadius = new CornerRadius(Math.Min(12, design.Corners)),
            BorderBrush = chosen ? Look.Accent : Look.Brush(design.WindowEdgeColor), BorderThickness = new Thickness(chosen ? 2.5 : 1),
            Cursor = new Cursor(StandardCursorType.Hand), Child = face,
        };
        ToolTip.SetTip(tile, design.Description);
        return tile;
    }

    /// <summary>Starts Holm anew (it waits for this one to let go) and closes this one.</summary>
    void RestartHolm()
    {
        try
        {
            if (Environment.ProcessPath is not { } path) return;
            Process.Start(new ProcessStartInfo(path, "--restart") { UseShellExecute = false });
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex) { Diag.Error(ex); }
    }
}
