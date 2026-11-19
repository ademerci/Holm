using System;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The Beam tool of the open notch: everything Beam does, in one place. Send: files dragged onto the notch, or
/// chosen with a click, go to a friend, someone nearby, the shelf or your phone. Receive: photos and files from a
/// phone, or what a friend sent with a code you copied.
/// </summary>
public sealed partial class NotchWindow
{
    readonly StackPanel _beamPage = new() { IsVisible = false };
    NotchButton _beamToolBtn = null!;

    Control BuildBeamPage()
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };

        // Send: a place to drop files, as tall as the receive side. Files dropped anywhere on the notch go the same way.
        var send = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        send.Children.Add(PageTitle("Send"));
        var words = new TextBlock
        {
            FontSize = 13, Foreground = Dim, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12),
            Inlines = new InlineCollection { new Run("Drag files here, or "), new Run("click to upload.") { Foreground = ShareBlue } },
        };
        var dashes = new Rectangle
        {
            Stroke = Look.InkA(0x40), StrokeThickness = 1.2, StrokeDashArray = new AvaloniaList<double> { 4, 3 },
            RadiusX = Look.Pick(3, 14), RadiusY = Look.Pick(3, 14),
        };
        var inside = new Grid { Children = { dashes, words } };
        var zone = new NotchButton(inside)
        {
            CornerRadius = new CornerRadius(Look.Pick(3, 14)), MinHeight = 80, VerticalAlignment = VerticalAlignment.Stretch,
        };
        inside.VerticalAlignment = VerticalAlignment.Stretch; // the dashes fill the zone (a button centres its content)
        inside.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(zone, "Choose files to send");
        zone.Click += PickFilesToSend;
        Grid.SetRow(zone, 1);
        send.Children.Add(zone);
        columns.Children.Add(send);

        var receive = new StackPanel();
        receive.Children.Add(PageTitle("Receive"));
        var phone = BeamCard("", PhoneIndigo, "From your phone", "iPhone or Android, with a QR code");
        phone.Click += ReceiveFromPhone;
        receive.Children.Add(phone);
        var code = BeamCard("", Dim, "With a copied code", "Copy a friend's code, then click");
        code.Margin = new Thickness(0, 6, 0, 0);
        code.Click += () => _ = ReceiveCopiedCodeAsync();
        receive.Children.Add(code);
        Grid.SetColumn(receive, 2);
        columns.Children.Add(receive);

        _beamPage.Children.Add(columns);
        return _beamPage;
    }

    static NotchButton BeamCard(string glyph, Avalonia.Media.IBrush color, string title, string more)
    {
        var titleText = CardTitle();
        titleText.Text = title;
        var moreText = CardMore();
        moreText.Text = more;
        moreText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        moreText.TextTrimming = Avalonia.Media.TextTrimming.None;
        return Card(glyph, color, titleText, moreText);
    }

    /// <summary>Click to upload: the system's file picker, then the share view to click where they go.</summary>
    async void PickFilesToSend()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose files to send", AllowMultiple = true });
            var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            if (paths.Length > 0) PickTarget(paths, patient: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Diag.Log("Beam files: " + ex.GetType().Name); }
    }
}
