using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinNotch;

/// <summary>What the AI button opens. <see cref="Web"/> is the website to fall back on, if there is one.</summary>
internal sealed record AiTarget(string Key, string Name, string Launch, string? Web, ImageSource? Icon);

/// <summary>
/// The AI button: Claude, Gemini, ChatGPT or any program you pick. A preset opens its installed app
/// (desktop, Store, or a web app installed from Edge or Chrome) and falls back to its website.
/// </summary>
internal static class AiLauncher
{
    public const string Custom = "custom";

    sealed record Preset(string Key, string Name, string[] AppNames, string Web);

    static readonly Preset[] Presets =
    {
        new("claude", "Claude", new[] { "Claude" }, "https://claude.ai/new"),
        new("gemini", "Gemini", new[] { "Gemini", "Google Gemini" }, "https://gemini.google.com/app"),
        new("chatgpt", "ChatGPT", new[] { "ChatGPT" }, "https://chatgpt.com/"),
    };

    public static IEnumerable<(string Key, string Name)> Choices => Presets.Select(p => (p.Key, p.Name));

    /// <summary>What to open right away, before the app list has been searched: the website, or the picked program.</summary>
    public static AiTarget? Quick(string? key, string? path)
    {
        if (key == Custom)
            return string.IsNullOrEmpty(path) ? null : new AiTarget(Custom, NameOf(path), path, null, null);
        return Presets.FirstOrDefault(p => p.Key == key) is { } preset
            ? new AiTarget(preset.Key, preset.Name, preset.Web, null, null)
            : null;
    }

    /// <summary>Looks for the installed app and its icon. Runs on a shell thread; takes a few hundred ms.</summary>
    public static Task<AiTarget?> ResolveAsync(string? key, string? path)
    {
        var quick = Quick(key, path);
        if (quick == null) return Task.FromResult<AiTarget?>(null);
        return Shell.RunAsync<AiTarget?>(() =>
        {
            try
            {
                if (key == Custom) return quick with { Icon = Shell.IconOf(quick.Launch, 64) };
                var preset = Presets.First(p => p.Key == key);
                if (Shell.FindApp(preset.AppNames) is not { } app) return quick;
                string launch = Shell.AppTarget(app.Id);
                return quick with { Launch = launch, Web = preset.Web, Icon = Shell.IconOf(launch, 64) };
            }
            catch (Exception ex)
            {
                Diag.Log("AI app lookup failed: " + ex.Message);
                return quick;
            }
        });
    }

    /// <summary>"Notion" for Notion.lnk, the product name for an exe ("Claude" rather than "claude").</summary>
    public static string NameOf(string path)
    {
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>The app's own icon when we have one, otherwise a drawn mark for the preset.</summary>
    public static FrameworkElement IconFor(AiTarget target, double size)
    {
        if (target.Icon != null)
        {
            var image = new Image { Source = target.Icon, Width = size, Height = size };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
        var mark = target.Key switch
        {
            "claude" => new System.Windows.Shapes.Path { Data = ClaudeMark.Value, Fill = Brush(0xD9, 0x77, 0x57) },
            "gemini" => new System.Windows.Shapes.Path { Data = GeminiMark.Value, Fill = GeminiBrush.Value },
            "chatgpt" => new System.Windows.Shapes.Path { Data = ChatGptMark.Value, Fill = Brushes.White },
            _ => null,
        };
        if (mark == null)
            return new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size * 0.85, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // The marks are drawn on a 24 × 24 grid.
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { mark } } };
    }

    // ───────────── marks, used when the app isn't installed (the button then opens the website) ─────────────

    static Brush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
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
                c.BeginFigure(centre + across * 1.55, true, true);
                c.LineTo(centre + along * (length - 0.6) + across * 0.45, false, false);
                c.QuadraticBezierTo(centre + along * length, centre + along * (length - 0.6) - across * 0.45, false, false);
                c.LineTo(centre - across * 1.55, false, false);
            }
        }
        geometry.Freeze();
        return geometry;
    });

    /// <summary>Gemini: the four-pointed sparkle in its blue-to-rose gradient.</summary>
    static readonly Lazy<Geometry> GeminiMark = new(() =>
    {
        var geometry = Geometry.Parse("M12,0 C12.8,6.2 17.8,11.2 24,12 C17.8,12.8 12.8,17.8 12,24 C11.2,17.8 6.2,12.8 0,12 C6.2,11.2 11.2,6.2 12,0 Z");
        geometry.Freeze();
        return geometry;
    });

    static readonly Lazy<Brush> GeminiBrush = new(() =>
    {
        var brush = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x47, 0x96, 0xE3), 0),
            new(Color.FromRgb(0x91, 0x77, 0xC7), 0.55),
            new(Color.FromRgb(0xCA, 0x66, 0x73), 1),
        }, new Point(0, 1), new Point(1, 0));
        brush.Freeze();
        return brush;
    });

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
                all = all == null ? capsule : Geometry.Combine(all, capsule, GeometryCombineMode.Union, null);
            }
            return all!;
        }
        var outline = Geometry.Combine(Petals(0), Petals(1.5), GeometryCombineMode.Exclude, null);
        outline.Freeze();
        return outline;
    });
}
