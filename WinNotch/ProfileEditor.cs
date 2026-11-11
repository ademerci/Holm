using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>The profile as edited, before Apply. Picture is the small JPEG in base64.</summary>
internal sealed record ProfileDraft(string? Name, string? Status, string? Color, string? Picture)
{
    /// <summary>The same profile once saved: empty text counts as none, colors are written one way.</summary>
    public ProfileDraft Clean() => new(FriendProfile.CleanText(Name, FriendProfile.MaxName), FriendProfile.CleanText(Status, FriendProfile.MaxStatus),
                                       FriendProfile.CleanColor(Color), Picture);
}

/// <summary>
/// The profile at the top of the settings window: your picture, name, status and color. Like the switches below it,
/// it's a draft until Apply.
/// </summary>
internal sealed class ProfileEditor : Border
{
    static readonly Brush Card = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    static readonly Brush Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));

    readonly string _windowsName;
    readonly Func<ProfileDraft> _saved;
    readonly Action<ProfileDraft> _apply;
    readonly Border _avatar;
    readonly TextBlock _initials;
    readonly TextBox _name, _status;
    readonly StackPanel _swatches;
    readonly Button _remove;
    ProfileDraft _original;
    string? _color, _picture;
    bool _loading;

    /// <summary>Something in the draft changed (to enable Apply).</summary>
    public event Action? Changed;

    public bool HasChanges => Draft().Clean() != _original.Clean();

    /// <param name="windowsName">The name used when none is typed, shown as the name box's hint.</param>
    /// <param name="saved">The profile as saved.</param>
    /// <param name="apply">Saves the profile and sends it to friends.</param>
    public ProfileEditor(string windowsName, Func<ProfileDraft> saved, Action<ProfileDraft> apply, Func<Style> buttonStyle)
    {
        _windowsName = windowsName;
        _saved = saved;
        _apply = apply;
        _original = saved();
        Background = Card;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(14);
        Padding = new Thickness(18);
        VerticalAlignment = VerticalAlignment.Top;

        // Picture | name and status side by side, then the colors and the picture buttons in one row.
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _initials = new TextBlock
        {
            FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _avatar = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Child = _initials, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 16, 0), ToolTip = "Choose a picture",
        };
        _avatar.MouseLeftButtonUp += (_, _) => ChoosePicture();
        grid.Children.Add(_avatar);

        var right = new StackPanel();
        var texts = new Grid();
        texts.ColumnDefinitions.Add(new ColumnDefinition());
        texts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        texts.ColumnDefinitions.Add(new ColumnDefinition());
        var nameColumn = new StackPanel();
        var statusColumn = new StackPanel();
        Grid.SetColumn(statusColumn, 2);
        texts.Children.Add(nameColumn);
        texts.Children.Add(statusColumn);
        _name = Field(nameColumn, "Name", windowsName);
        _status = Field(statusColumn, "Status", "e.g. 🎧 in focus, back at 3");
        _name.MaxLength = FriendProfile.MaxName;
        _status.MaxLength = FriendProfile.MaxStatus * 2; // emoji take two; the saved text is cut by whole characters
        right.Children.Add(texts);

        var lower = new DockPanel { LastChildFill = false };
        _swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var color in MainWindow.AvatarColors) _swatches.Children.Add(Swatch(color));
        lower.Children.Add(_swatches);
        var choose = new Button { Style = buttonStyle(), Content = "Choose picture…", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        choose.Click += (_, _) => ChoosePicture();
        _remove = new Button { Style = buttonStyle(), Content = "Remove picture", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        _remove.Click += (_, _) => { _picture = null; Show(); };
        DockPanel.SetDock(_remove, Dock.Right);
        DockPanel.SetDock(choose, Dock.Right);
        lower.Children.Add(_remove);
        lower.Children.Add(choose);
        right.Children.Add(lower);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        var all = new StackPanel();
        all.Children.Add(grid);
        all.Children.Add(new TextBlock
        {
            Text = "Your friends see your picture, name and status next to Beam. Only they get it, sealed like every friend note.",
            Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
        });
        Child = all;
        _name.TextChanged += (_, _) => Show();
        _status.TextChanged += (_, _) => Show();
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
        string name = FriendProfile.CleanText(_name.Text, FriendProfile.MaxName) ?? _windowsName;
        var picture = ProfilePicture.BrushOf(_picture);
        _avatar.Background = picture ?? (_color != null ? Frozen((Color)ColorConverter.ConvertFromString(_color)) : MainWindow.AvatarColor(name));
        _initials.Text = picture != null ? "" : MainWindow.Initials(name);
        _remove.IsEnabled = _picture != null;
        foreach (Border swatch in _swatches.Children)
        {
            bool chosen = _color != null && swatch.Tag is string hex && hex == _color;
            swatch.BorderBrush = chosen ? Brushes.White : Brushes.Transparent;
        }
        if (!_loading) Changed?.Invoke();
    }

    Border Swatch(Color color)
    {
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var swatch = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Frozen(color), Tag = hex,
            BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
            ToolTip = "The color behind your initials (click again for the one your name gives)",
        };
        swatch.MouseLeftButtonUp += (_, _) => { _color = _color == hex ? null : hex; Show(); };
        return swatch;
    }

    void ChoosePicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose your profile picture",
            Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        try
        {
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        }
        catch (Exception ex) { Diag.Error(ex); return; }
        if (ProfilePicture.FromFile(dialog.FileName) is { } jpeg)
        {
            _picture = Convert.ToBase64String(jpeg);
            Show();
        }
        else MessageBox.Show(Window.GetWindow(this), "Holm can't read this picture. Try a JPEG or PNG.", "Profile picture",
                             MessageBoxButton.OK, MessageBoxImage.Information);
    }

    static TextBox Field(Panel parent, string label, string hint)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = Dim, FontSize = 11, Margin = new Thickness(2, 0, 0, 4) });
        var box = new TextBox
        {
            Foreground = Brushes.White, CaretBrush = Brushes.White, Background = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderBrush = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 6, 6, 6), FontSize = 13, Template = FieldTemplate(),
        };
        // The hint shows while the box is empty.
        var placeholder = new TextBlock
        {
            Text = hint, Foreground = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), IsHitTestVisible = false,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13,
        };
        var host = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        host.Children.Add(box);
        host.Children.Add(placeholder);
        box.TextChanged += (_, _) => placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        parent.Children.Add(host);
        return box;
    }

    static ControlTemplate FieldTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
        <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                         xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'>
            <Border x:Name='Box' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='7'>
                <ScrollViewer x:Name='PART_ContentHost' Margin='0,6' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property='IsKeyboardFocused' Value='True'>
                    <Setter TargetName='Box' Property='BorderBrush' Value='#FF0A84FF'/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>");

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
