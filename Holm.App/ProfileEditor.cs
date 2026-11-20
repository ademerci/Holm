using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>The profile as edited, before Apply. Picture is the small picture in base64.</summary>
internal sealed record ProfileDraft(string? Name, string? Status, string? Color, string? Picture)
{
    /// <summary>The same profile once saved: empty text counts as none, colors are written one way.</summary>
    public ProfileDraft Clean() => new(FriendProfile.CleanText(Name, FriendProfile.MaxName), FriendProfile.CleanText(Status, FriendProfile.MaxStatus),
                                       FriendProfile.CleanColor(Color), Picture);
}

/// <summary>
/// The profile in the Holm window (the WPF app's ProfileEditor): your picture, name, status and color. Like the
/// switches, it's a draft until Apply.
/// </summary>
internal sealed class ProfileEditor : Border
{
    /// <summary>Pixels per side, as the WPF app's ProfilePicture: sharp in a 34 px circle, small enough for a note.</summary>
    const int PictureSize = 64;

    readonly string _accountName;
    readonly Func<ProfileDraft> _saved;
    readonly Action<ProfileDraft> _apply;
    readonly Func<string, IBrush> _nameColor;
    readonly Border _avatar;
    readonly TextBlock _initials;
    readonly TextBox _name, _status;
    readonly StackPanel _swatches;
    readonly Button _remove;
    readonly TextBlock _problem = new() { Foreground = HubTheme.Red, FontSize = 11, Margin = new Thickness(0, 6, 0, 0), IsVisible = false };
    ProfileDraft _original;
    string? _color, _picture;
    bool _loading;

    /// <summary>Something in the draft changed (to enable Apply).</summary>
    public event Action? Changed;

    /// <summary>Enter in the name or status box: apply, like a form.</summary>
    public event Action? Submit;

    public bool HasChanges => Draft().Clean() != _original.Clean();

    public ProfileEditor(string accountName, Func<ProfileDraft> saved, Action<ProfileDraft> apply, Color[] colors, Func<string, IBrush> nameColor)
    {
        _accountName = accountName;
        _saved = saved;
        _apply = apply;
        _nameColor = nameColor;
        _original = saved();
        Background = HubTheme.Card;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(18);
        VerticalAlignment = VerticalAlignment.Top;

        // Picture | name and status side by side, then the colors and the picture buttons in one row.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        _initials = new TextBlock { FontSize = 22, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _avatar = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Child = _initials, Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 16, 0), ClipToBounds = true,
        };
        ToolTip.SetTip(_avatar, "Choose a picture");
        _avatar.PointerReleased += (_, _) => ChoosePicture();
        grid.Children.Add(_avatar);

        var right = new StackPanel();
        var texts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var nameColumn = new StackPanel();
        var statusColumn = new StackPanel();
        Grid.SetColumn(statusColumn, 2);
        texts.Children.Add(nameColumn);
        texts.Children.Add(statusColumn);
        _name = Field(nameColumn, "Name", accountName);
        _status = Field(statusColumn, "Status", "e.g. 🎧 in focus, back at 3");
        _name.MaxLength = FriendProfile.MaxName;
        _status.MaxLength = FriendProfile.MaxStatus * 2; // emoji take two; the saved text is cut by whole characters
        right.Children.Add(texts);

        var lower = new DockPanel { LastChildFill = false };
        _swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var color in colors) _swatches.Children.Add(Swatch(color));
        lower.Children.Add(_swatches);
        var choose = HubTheme.Action("Choose picture…");
        choose.Padding = new Thickness(12, 4, 12, 4);
        choose.Margin = new Thickness(8, 0, 0, 0);
        choose.Click += (_, _) => ChoosePicture();
        _remove = HubTheme.Action("Remove picture");
        _remove.Padding = new Thickness(12, 4, 12, 4);
        _remove.Margin = new Thickness(8, 0, 0, 0);
        _remove.Click += (_, _) => { _picture = null; Show(); };
        DockPanel.SetDock(_remove, Dock.Right);
        DockPanel.SetDock(choose, Dock.Right);
        lower.Children.Add(_remove);
        lower.Children.Add(choose);
        right.Children.Add(lower);
        right.Children.Add(_problem);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        var all = new StackPanel();
        all.Children.Add(grid);
        all.Children.Add(new TextBlock
        {
            Text = "Your friends see your picture, name and status next to Beam. Only they get it, sealed like every friend note.",
            Foreground = HubTheme.Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
        });
        Child = all;
        _name.TextChanged += (_, _) => Show();
        _status.TextChanged += (_, _) => Show();
        foreach (var box in new[] { _name, _status })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Submit?.Invoke(); } };
        Load(_original);
    }

    /// <summary>Shows the profile as saved, dropping the draft.</summary>
    public void Reload()
    {
        _original = _saved();
        Load(_original);
    }

    public void Apply()
    {
        if (!HasChanges) return;
        _apply(Draft());
        Reload();
    }

    ProfileDraft Draft() => new(_name.Text, _status.Text, _color, _picture);

    void Load(ProfileDraft profile)
    {
        _loading = true;
        _name.Text = profile.Name ?? "";
        _status.Text = profile.Status ?? "";
        _color = FriendProfile.CleanColor(profile.Color);
        _picture = profile.Picture;
        _loading = false;
        Show();
    }

    void Show()
    {
        string name = FriendProfile.CleanText(_name.Text, FriendProfile.MaxName) ?? _accountName;
        var picture = PictureBrush(_picture);
        _avatar.Background = picture ?? (_color != null && Color.TryParse(_color, out var c) ? new SolidColorBrush(c) : _nameColor(name));
        _initials.Text = picture != null ? "" : NotchWindow.Initials(name);
        _initials.Foreground = Look.InkOn(_avatar.Background);
        _remove.IsEnabled = _picture != null;
        foreach (var swatch in _swatches.Children.OfType<Border>())
            swatch.BorderBrush = _color != null && swatch.Tag is string hex && hex == _color ? Brushes.White : Brushes.Transparent;
        if (!_loading) Changed?.Invoke();
    }

    internal static IBrush? PictureBrush(string? base64)
    {
        if (base64 == null) return null;
        try { return new ImageBrush(new Bitmap(new MemoryStream(Convert.FromBase64String(base64)))) { Stretch = Stretch.UniformToFill }; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    Border Swatch(Color color)
    {
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var swatch = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(color), Tag = hex,
            BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 8, 0), Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(swatch, "The color behind your initials (click again for the one your name gives)");
        swatch.PointerReleased += (_, _) => { _color = _color == hex ? null : hex; Show(); };
        return swatch;
    }

    async void ChoosePicture()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose your profile picture", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Pictures") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp" } }, FilePickerFileTypes.All },
            });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            if (Encode(stream) is { } png)
            {
                _picture = Convert.ToBase64String(png);
                _problem.IsVisible = false;
                Show();
            }
            else Problem("Holm can't read this picture. Try a JPEG or PNG.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Problem("Holm can't read this picture. Try a JPEG or PNG."); }
    }

    void Problem(string text)
    {
        _problem.Text = text;
        _problem.IsVisible = true;
    }

    /// <summary>The middle square of the picture, 64 px, as a PNG.</summary>
    static byte[]? Encode(Stream source)
    {
        try
        {
            using var bitmap = new Bitmap(source);
            var size = bitmap.PixelSize;
            int side = Math.Min(size.Width, size.Height);
            if (side <= 0) return null;
            using var target = new RenderTargetBitmap(new PixelSize(PictureSize, PictureSize));
            using (var context = target.CreateDrawingContext())
            {
                var crop = new Rect((size.Width - side) / 2.0, (size.Height - side) / 2.0, side, side);
                context.DrawImage(bitmap, crop, new Rect(0, 0, PictureSize, PictureSize));
            }
            using var output = new MemoryStream();
            target.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    static TextBox Field(Panel parent, string label, string hint)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = HubTheme.Dim, FontSize = 11, Margin = new Thickness(2, 0, 0, 4) });
        var box = HubTheme.Field(hint);
        box.Margin = new Thickness(0, 0, 0, 8);
        parent.Children.Add(box);
        return box;
    }
}
