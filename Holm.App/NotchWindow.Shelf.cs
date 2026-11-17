using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The shelf (the WPF app's MainWindow.Shelf.cs, with its Shelf.cs linked): drop files on the notch to park them,
/// switch folders or windows, then drag them out of the open notch to where they should go. A small count shows in
/// the idle notch while something is on it. Dragging files over the notch opens the drop view, as in the WPF app;
/// Beam's people and codes are not ported, so its one place is the shelf.
/// </summary>
public sealed partial class NotchWindow
{
    const string ShelfGlyph = "", FolderGlyph = "", ErrorGlyph = "";
    const int ShelfShown = 8;
    static readonly IBrush ShelfGray = Brush("#FF8E8E93"), ChipBrush = Brush("#1AFFFFFF");

    readonly StackPanel _idleShelf = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right };
    readonly TextBlock _idleShelfCount = new() { FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Dim, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _shelfRow = new() { Margin = new Thickness(2, 12, 2, 0), IsVisible = false };
    readonly TextBlock _shelfTitle = new() { FontSize = 11, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll) };
    readonly WrapPanel _shelfItems = new() { Margin = new Thickness(0, 6, 0, 0) };

    // The drop view: what is being dragged over the notch, and where it can go.
    readonly StackPanel _shareView = new() { Width = 364, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0) };
    readonly TextBlock _shareTitle = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _shareHint = new() { FontSize = 11, Foreground = Dim, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly WrapPanel _shareTargets = new() { Margin = new Thickness(0, 10, 0, 0) };
    bool _sharing, _draggingOut;

    void BuildShelf()
    {
        _idleShelf.Children.Add(new TextBlock { Text = ShelfGlyph, FontFamily = IconFont, FontSize = 10, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
        _idleShelf.Children.Add(_idleShelfCount);
        _idleView.Children.Add(_idleShelf);

        var header = new Grid();
        ToolTip.SetTip(_shelfTitle, "Drag to take everything off the shelf at once");
        header.Children.Add(_shelfTitle);
        var clear = new NotchButton(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 10, Foreground = Dim })
        {
            Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Right,
        };
        ToolTip.SetTip(clear, "Clear the shelf (the files stay where they are)");
        clear.Click += () => { _settings.Shelf.Clear(); _settings.Save(); ShowShelf(); };
        header.Children.Add(clear);
        _shelfRow.Children.Add(header);
        _shelfRow.Children.Add(_shelfItems);
        _homePanel.Children.Add(_shelfRow);
        _shelfTitle.PointerPressed += (_, e) => _ = DragOffShelfAsync(e, ShelfItems.ToArray());

        _shareView.Children.Add(_shareTitle);
        _shareView.Children.Add(_shareHint);
        _shareView.Children.Add(_shareTargets);
        _islandContent.Children.Add(_shareView);
        _shareView.Opacity = 0;
        _shareView.IsHitTestVisible = false;

        int gone = Shelf.Prune(_settings.Shelf);
        if (gone > 0)
        {
            _settings.Save();
            Diag.Log($"Shelf: {gone} item(s) moved or deleted meanwhile");
        }
        ShowShelf();
    }

    IReadOnlyList<string> ShelfItems => _settings.Shelf;

    static List<string> DroppedPaths(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToList() ?? new List<string>();

    /// <summary>The WPF app's ChipButton: a round avatar and a name on a soft pill (a small tile in the instrument look).</summary>
    static Border Chip(Control avatar, string name)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(avatar);
        content.Children.Add(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeight.SemiBold, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        return new Border { Background = ChipBrush, CornerRadius = new CornerRadius(Look.Pick(4, 19)), Padding = new Thickness(4, 4, 14, 4), Margin = new Thickness(0, 0, 8, 8), Child = content };
    }

    void AddToShelf(IReadOnlyCollection<string> paths)
    {
        int added = Shelf.Add(_settings.Shelf, paths);
        _settings.Save();
        ShowShelf();
        int count = _settings.Shelf.Count;
        string title = added == 0 ? "Already on the shelf" : added == 1 ? "On the shelf" : $"{added} items on the shelf";
        ShowPeek(ShelfGlyph, title, count == 1 ? "1 item" : $"{count} items", Dim);
    }

    void RemoveFromShelf(IEnumerable<string> paths)
    {
        if (Shelf.Remove(_settings.Shelf, paths) == 0) return;
        _settings.Save();
        ShowShelf();
    }

    /// <summary>The count in the idle notch, and the row in the open one.</summary>
    void ShowShelf()
    {
        var items = ShelfItems;
        _idleShelfCount.Text = items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ShowIdleStatus(); // the shelf shows unless a reminder is due today
        _shelfRow.IsVisible = items.Count > 0;
        _shelfItems.Children.Clear();
        _shelfTitle.Text = items.Count == 1 ? "Shelf · 1 item" : $"Shelf · {items.Count} items";
        foreach (string path in items.Reverse().Take(ShelfShown)) // the newest first
            _shelfItems.Children.Add(ShelfChip(path));
        if (items.Count > ShelfShown)
            _shelfItems.Children.Add(new TextBlock { Text = $"+{items.Count - ShelfShown}", FontSize = 12, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 6) });
        if (_state == NotchState.Expanded) ApplyState(animate: true); // the row may have grown or wrapped
    }

    Control ShelfChip(string path)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = Directory.Exists(path) ? FolderGlyph : "", FontFamily = IconFont, FontSize = 14, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = Shelf.DisplayName(path), FontSize = 12, MaxWidth = 130, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0),
        });
        var chip = new Border
        {
            Background = ChipBrush, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 5, 10, 5),
            Margin = new Thickness(0, 0, 6, 6), Child = content, Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(chip, path + "\nDrag it where it should go, or click to open it");
        // A press that moves becomes a drag out of the notch; one that doesn't opens the file.
        PointerPressedEventArgs? press = null;
        chip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            press = e;
            e.Handled = true;
        };
        chip.PointerMoved += (_, e) =>
        {
            if (press is not { } pressed || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var start = pressed.GetPosition(this);
            var at = e.GetPosition(this);
            if (Math.Abs(at.X - start.X) < 4 && Math.Abs(at.Y - start.Y) < 4) return;
            press = null;
            _ = DragOffShelfAsync(pressed, new[] { path });
        };
        chip.PointerReleased += (_, e) =>
        {
            if (press == null) return;
            press = null;
            e.Handled = true;
            SetExpanded(false);
            try { _shell.Open(path); }
            catch (Exception) { ShowPeek(ErrorGlyph, "Can't open " + Shelf.DisplayName(path), "", null); }
        };
        return chip;
    }

    /// <summary>
    /// Drags the files out: the app they're dropped on decides whether they're moved or copied. Either way they leave
    /// the shelf; so do files a drop moved without saying so.
    /// </summary>
    async Task DragOffShelfAsync(PointerPressedEventArgs press, string[] paths)
    {
        var present = paths.Where(Shelf.Exists).ToArray();
        if (present.Length < paths.Length) RemoveFromShelf(paths.Except(present));
        if (present.Length == 0 || StorageProvider is not { } storage) return;

        var data = new DataTransfer();
        foreach (string path in present)
        {
            IStorageItem? item = Directory.Exists(path)
                ? await storage.TryGetFolderFromPathAsync(new Uri(path))
                : await storage.TryGetFileFromPathAsync(new Uri(path));
            if (item != null) data.Add(DataTransferItem.CreateFile(item));
        }
        var result = DragDropEffects.None;
        _draggingOut = true;
        try { result = await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
        catch (Exception ex) { Diag.Log("Shelf drag failed: " + ex.GetType().Name); }
        finally { _draggingOut = false; }
        Diag.Log($"Dragged {present.Length} item(s) off the shelf: {result}");
        RemoveFromShelf(result != DragDropEffects.None ? present : present.Where(p => !Shelf.Exists(p)));
    }
}
