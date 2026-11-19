using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The AI launcher at the bottom right of the open notch. A preset opens its website, and a custom target opens the selected program.
/// </summary>
public sealed partial class NotchWindow
{
    const string AiCustom = "custom";

    sealed record AiPreset(string Key, string Name, string Web)
    {
        // Marks from Lobe Icons, bundled so they work offline and on both platforms.
        public Lazy<Bitmap> Icon { get; } = new(() =>
        {
            using var stream = AssetLoader.Open(new Uri($"avares://Holm.App/Assets/AiLauncher/{Key}.png"));
            return new Bitmap(stream);
        });
    }

    static readonly AiPreset[] AiPresets =
    {
        new("claude", "Claude", "https://claude.ai/new"),
        new("gemini", "Gemini", "https://gemini.google.com/app"),
        new("chatgpt", "ChatGPT", "https://chatgpt.com/"),
        new("copilot", "Microsoft Copilot", "https://copilot.microsoft.com/"),
        new("github-copilot", "GitHub Copilot", "https://github.com/copilot"),
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

    /// <summary>Presets, the selected custom app, and Off.</summary>
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
        if (key != null && AiTile(key, 16) is { } icon) item.Icon = icon;
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

    static Border? AiTile(string key, double size)
    {
        if (AiPresets.FirstOrDefault(preset => preset.Key == key) is not { } preset) return null;
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Background = Brushes.White,
            Child = new Image
            {
                Source = preset.Icon.Value, Width = size * 0.76, Height = size * 0.76,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}
