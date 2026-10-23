using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinNotch;

/// <summary>
/// The shelf in the notch: drop files on "Shelf" to park them, switch folders or windows, then drag them out of the
/// open notch to where they should go. A small count shows in the idle notch while something is on it.
/// </summary>
public partial class MainWindow
{
    const int ShelfShown = 8;
    static readonly Brush ShelfGray = Frozen(Color.FromRgb(0x8E, 0x8E, 0x93));

    bool _draggingOut;
    FrameworkElement? _shelfPressed;
    Point _shelfPressedAt;
    IReadOnlyList<string>? _demoShelf = null; // README screenshots only (set in Debug builds)

    IReadOnlyList<string> ShelfItems => _demoShelf ?? _settings.Shelf;

    void InitShelf()
    {
        int gone = Shelf.Prune(_settings.Shelf);
        if (gone > 0)
        {
            _settings.Save();
            Diag.Log($"Shelf: {gone} item(s) moved or deleted meanwhile");
        }
        ShowShelf();
    }

    void AddToShelf(IReadOnlyCollection<string> paths)
    {
        int added = Shelf.Add(_settings.Shelf, paths);
        _settings.Save();
        ShowShelf();
        int count = _settings.Shelf.Count;
        string title = added == 0 ? "Already on the shelf" : added == 1 ? "On the shelf" : $"{added} items on the shelf";
        ShowPeek("", title, count == 1 ? "1 item" : $"{count} items", null, false, DimBrush);
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
        IdleShelf.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        IdleShelfCount.Text = items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_expanded) RefreshShelfRow();
    }

    void RefreshShelfRow()
    {
        var items = ShelfItems;
        ShelfRow.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfItemsPanel.Children.Clear();
        ShelfTitle.Text = items.Count == 1 ? "Shelf · 1 item" : $"Shelf · {items.Count} items";
        foreach (string path in items.Reverse().Take(ShelfShown)) // the newest first
            ShelfItemsPanel.Children.Add(ShelfChip(path));
        if (items.Count > ShelfShown)
            ShelfItemsPanel.Children.Add(new TextBlock
            {
                Text = $"+{items.Count - ShelfShown}", FontSize = 12, Foreground = DimBrush,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 6),
            });
        if (_expanded && _state == NotchState.Expanded) ApplyState(); // the row may have grown or wrapped
    }

    FrameworkElement ShelfChip(string path)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (FileIcons.For(path) is { } icon)
        {
            var image = new Image { Source = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            content.Children.Add(image);
        }
        content.Children.Add(new TextBlock
        {
            Text = Shelf.DisplayName(path), FontSize = 12, MaxWidth = 130, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0),
        });
        var chip = new Border
        {
            Background = ChipBrush, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 5, 10, 5),
            Margin = new Thickness(0, 0, 6, 6), Child = content, Tag = path, Cursor = Cursors.Hand,
            ToolTip = path + "\nDrag it where it should go, or click to open it",
        };
        chip.MouseLeftButtonDown += Shelf_MouseDown;
        chip.MouseMove += Shelf_MouseMove;
        chip.MouseLeftButtonUp += ShelfItem_MouseUp;
        return chip;
    }

    /// <summary>On an item: that one; on the title: everything on the shelf.</summary>
    void Shelf_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _shelfPressed = (FrameworkElement)sender;
        _shelfPressedAt = e.GetPosition(this);
        e.Handled = true;
    }

    void Shelf_MouseMove(object sender, MouseEventArgs e)
    {
        if (_shelfPressed is not { } source || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - _shelfPressedAt.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(at.Y - _shelfPressedAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _shelfPressed = null;
        DragOffShelf(source, source.Tag is string one ? new[] { one } : ShelfItems.ToArray());
    }

    void ShelfItem_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(_shelfPressed, sender)) return;
        _shelfPressed = null;
        e.Handled = true;
        if (((FrameworkElement)sender).Tag is not string path) return;
        SetExpanded(false);
        if (!TryOpen(path)) ShowPeek("", "Can't open " + Shelf.DisplayName(path), "", null, false, null);
    }

    /// <summary>
    /// Drags the files out like Explorer does: dropped on a folder on the same drive they move, elsewhere they're
    /// copied. Either way they leave the shelf; so do files a drop moved without saying so (Explorer does that).
    /// </summary>
    void DragOffShelf(DependencyObject source, string[] paths)
    {
        var present = paths.Where(Shelf.Exists).ToArray();
        if (present.Length < paths.Length) RemoveFromShelf(paths.Except(present));
        if (present.Length == 0) return;

        var data = new DataObject(DataFormats.FileDrop, present);
        var result = DragDropEffects.None;
        _draggingOut = true;
        try { result = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
        catch (COMException ex) { Diag.Log("Shelf drag failed: " + ex.Message); }
        finally { _draggingOut = false; }
        Diag.Log($"Dragged {present.Length} item(s) off the shelf: {result}");
        RemoveFromShelf(result != DragDropEffects.None ? present : present.Where(p => !Shelf.Exists(p)));
    }

    void ShelfClear_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _settings.Shelf.Clear();
        _settings.Save();
        ShowShelf();
    }
}
