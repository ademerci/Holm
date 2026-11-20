using System.Text.Json;

namespace WinNotch.Tests;

public class NotchPlacementTests
{
    [Theory]
    [InlineData(0, 1920, 560, null, 680)]
    [InlineData(-1920, 0, 560, 0.5, -1240)]
    [InlineData(0, 1920, 560, 0.0, 0)]
    [InlineData(0, 1920, 560, 1.0, 1360)]
    [InlineData(0, 400, 560, 0.9, 0)]
    [InlineData(0, 1920, 840, 0.75, 1020)]
    public void Placement_stays_on_the_selected_display(int left, int right, int width, double? fraction, int expected)
        => Assert.Equal(expected, NotchPlacement.Left(left, right, width, fraction));

    [Theory]
    [InlineData(1.3, 540, 607, 605)]
    [InlineData(1.5, 700, 400, 398)]
    [InlineData(1.25, 540, 1920, 675)]
    public void Scale_keeps_the_shape_inside_the_display(double scale, double shapeWidth, double displayWidth, double fittedWidth)
        => Assert.Equal(fittedWidth, NotchPlacement.FitScale(scale, shapeWidth, displayWidth) * shapeWidth, 8);

    [Fact]
    public void Saved_position_survives_restart_and_disabling_dragging()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.False(settings.Draggable);
        Assert.Null(settings.NotchPosition);
        settings.Draggable = true;
        settings.NotchPosition = NotchPlacement.Fraction(-1920, 0, 560, -900);
        settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(settings.Draggable);
        Assert.Equal(-900, NotchPlacement.Left(-1920, 0, 560, settings.NotchPosition));
        settings.Draggable = false;
        settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.False(settings.Draggable);
        Assert.Equal(-900, NotchPlacement.Left(-1920, 0, 560, settings.NotchPosition));
        settings.NotchPosition = null;
        Assert.Equal(-1240, NotchPlacement.Left(-1920, 0, 560, settings.NotchPosition));
    }

    [Fact]
    public void Invalid_saved_position_falls_back_to_the_centre()
    {
        Assert.Equal(680, NotchPlacement.Left(0, 1920, 560, double.NaN));
        Assert.Equal(680, NotchPlacement.Left(0, 1920, 560, double.PositiveInfinity));
    }
}
