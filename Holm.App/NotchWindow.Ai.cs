using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The AI button (the WPF app's AiLauncher and its part of MainWindow.xaml.cs): Claude, Gemini, ChatGPT or any program
/// you pick, at the bottom right of the open notch. A preset opens its website; a program you picked starts itself.
/// </summary>
public sealed partial class NotchWindow
{
    const string AiCustom = "custom";

    sealed record AiPreset(string Key, string Name, string Web);

    static readonly AiPreset[] AiPresets =
    {
        new("claude", "Claude", "https://claude.ai/new"),
        new("gemini", "Gemini", "https://gemini.google.com/app"),
        new("chatgpt", "ChatGPT", "https://chatgpt.com/"),
    };

    NotchButton _aiBtn = null!;
    readonly Border _aiIconHost = new() { Width = 18, Height = 18 };

    void BuildAiButton(Panel sound)
    {
        _aiBtn = new NotchButton(_aiIconHost) { Width = 30, Height = 30, Margin = new Thickness(6, 0, 0, 0), IsVisible = false };
        _aiBtn.Click += OpenAi;
        sound.Children.Add(_aiBtn);
        UpdateAiButton();
    }

    void UpdateAiButton()
    {
        string? name = AiName();
        _aiBtn.IsVisible = name != null;
        if (name == null) return;
        _aiIconHost.Child = (Control?)AiTile(_settings.Ai ?? "", 18) ?? new TextBlock
        {
            Text = "", FontFamily = IconFont, FontSize = 18 * 0.85, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(_aiBtn, "Open " + name);
    }

    string? AiName() => _settings.Ai == AiCustom
        ? string.IsNullOrEmpty(_settings.AiPath) ? null : System.IO.Path.GetFileNameWithoutExtension(_settings.AiPath)
        : AiPresets.FirstOrDefault(p => p.Key == _settings.Ai)?.Name;

    void OpenAi()
    {
        string? target = _settings.Ai == AiCustom ? _settings.AiPath : AiPresets.FirstOrDefault(p => p.Key == _settings.Ai)?.Web;
        if (target == null) return;
        // Close up, so the notch isn't in front of the app that opens.
        SetExpanded(false);
        try
        {
            if (_settings.Ai == AiCustom && OperatingSystem.IsLinux() && target.EndsWith(".desktop", StringComparison.Ordinal))
            {
                if (!_shell.OpenApp(System.IO.Path.GetFileNameWithoutExtension(target))) throw new IOException();
            }
            else if (_settings.Ai == AiCustom && OperatingSystem.IsLinux() && File.Exists(target))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = false })?.Dispose();
            else _shell.Open(target);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Diag.Log("AI button: " + ex.GetType().Name);
            ShowPeek("", "Can't open " + AiName(), "", null);
        }
    }

    void SetAi(string? key, string? path)
    {
        _settings.Ai = key;
        _settings.AiPath = path;
        _settings.Save();
        UpdateAiButton();
    }

    /// <summary>AI button › Claude, Gemini, ChatGPT, the app you picked, Choose app…, Off.</summary>
    void FillAiMenu(MenuItem menu)
    {
        menu.Items.Clear();
        foreach (var preset in AiPresets) menu.Items.Add(AiChoice(preset.Name, preset.Key, _settings.AiPath));
        if (!string.IsNullOrEmpty(_settings.AiPath))
            menu.Items.Add(AiChoice(System.IO.Path.GetFileNameWithoutExtension(_settings.AiPath).Replace("_", "__"), AiCustom, _settings.AiPath));
        menu.Items.Add(new Separator());
        var pick = new MenuItem { Header = "Choose app…" };
        // After the menu has closed: a dialog from inside the click would open under the closing menu.
        pick.Click += (_, _) => Dispatcher.UIThread.Post(PickAiApp, DispatcherPriority.Background);
        menu.Items.Add(pick);
        menu.Items.Add(AiChoice("Off", null, _settings.AiPath));
    }

    MenuItem AiChoice(string header, string? key, string? path)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = _settings.Ai == key };
        item.Click += (_, _) => SetAi(key, path);
        return item;
    }

    async void PickAiApp()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose the app the AI button opens", AllowMultiple = false,
                FileTypeFilter = OperatingSystem.IsWindows()
                    ? new[] { new FilePickerFileType("Apps and shortcuts") { Patterns = new[] { "*.exe", "*.lnk", "*.url", "*.appref-ms" } }, FilePickerFileTypes.All }
                    : new[] { new FilePickerFileType("Apps") { Patterns = new[] { "*.desktop", "*.AppImage", "*" } } },
                SuggestedStartLocation = OperatingSystem.IsLinux() ? await StorageProvider.TryGetFolderFromPathAsync("/usr/share/applications") : null,
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) SetAi(AiCustom, path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Diag.Log("AI app: " + ex.GetType().Name); }
    }

    // ───────────── the presets' tiles, drawn on a 24 × 24 grid ─────────────

    /// <summary>A preset as a favicon-style tile (its mark, light on its brand color); null for anything else.</summary>
    static Border? AiTile(string key, double size)
    {
        (Geometry Mark, IBrush Fill, IBrush Back)? look = key switch
        {
            "claude" => (ClaudeMark.Value, Brush("#FFFFF6EE"), Brush("#FFD97757")),
            "gemini" => (GeminiMark.Value, Brushes.White, GeminiBrush.Value),
            "chatgpt" => (ChatGptMark.Value, Brush("#FF0D0D0D"), Brush("#FFF4F4F4")),
            _ => null,
        };
        if (look is not { } l) return null;
        // The mark takes two thirds of the tile, centred: the margin a favicon keeps.
        var mark = new Viewbox
        {
            Width = size * 0.68, Height = size * 0.68, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new Canvas { Width = 24, Height = 24, Children = { new Avalonia.Controls.Shapes.Path { Data = l.Mark, Fill = l.Fill } } },
        };
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Background = l.Back, Child = mark };
    }

    /// <summary>Claude: a hand-drawn looking starburst.</summary>
    static readonly Lazy<Geometry> ClaudeMark = new(() =>
    {
        double[] lengths = { 11.5, 9, 10.7, 9.4, 11.5, 9.9, 10.9, 8.7, 11.3, 9.7, 10.4, 9.2 };
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            for (int i = 0; i < lengths.Length; i++)
            {
                double angle = (i * 30 - 90 + (i % 2 == 1 ? 3 : 0)) * Math.PI / 180;
                var along = new Vector(Math.Cos(angle), Math.Sin(angle));
                var across = new Vector(-along.Y, along.X);
                var centre = new Point(12, 12);
                double length = lengths[i];
                // A ray: wide at the centre, narrowing to a rounded tip.
                c.BeginFigure(centre + across * 1.55, true);
                c.LineTo(centre + along * (length - 0.6) + across * 0.45);
                c.QuadraticBezierTo(centre + along * length, centre + along * (length - 0.6) - across * 0.45);
                c.LineTo(centre - across * 1.55);
                c.EndFigure(true);
            }
        }
        return geometry;
    });

    /// <summary>Gemini: the four-pointed sparkle in its blue-to-rose gradient.</summary>
    static readonly Lazy<Geometry> GeminiMark = new(() =>
        Geometry.Parse("M12,0 C12.8,6.2 17.8,11.2 24,12 C17.8,12.8 12.8,17.8 12,24 C11.2,17.8 6.2,12.8 0,12 C6.2,11.2 11.2,6.2 12,0 Z"));

    static readonly Lazy<IBrush> GeminiBrush = new(() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromRgb(0x47, 0x96, 0xE3), 0),
            new GradientStop(Color.FromRgb(0x91, 0x77, 0xC7), 0.55),
            new GradientStop(Color.FromRgb(0xCA, 0x66, 0x73), 1),
        },
    }.ToImmutable());

    /// <summary>ChatGPT: the outline of six interlocking capsules, a simplified blossom.</summary>
    static readonly Lazy<Geometry> ChatGptMark = new(() =>
    {
        Geometry Petals(double shrink)
        {
            Geometry? all = null;
            for (int k = 0; k < 6; k++)
            {
                double angle = k * 60 * Math.PI / 180;
                double length = 13 - 2 * shrink, thickness = 6.4 - 2 * shrink;
                var capsule = new RectangleGeometry(new Rect(-length / 2, -thickness / 2, length, thickness), thickness / 2, thickness / 2)
                {
                    Transform = new TransformGroup
                    {
                        Children = { new RotateTransform(k * 60 + 120), new TranslateTransform(12 - Math.Sin(angle) * 3, 12 + Math.Cos(angle) * 3) },
                    },
                };
                all = all == null ? capsule : new CombinedGeometry(GeometryCombineMode.Union, all, capsule);
            }
            return all!;
        }
        return new CombinedGeometry(GeometryCombineMode.Exclude, Petals(0), Petals(1.5));
    });
}
