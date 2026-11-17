using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinNotch.Tests;

[CollectionDefinition("Look", DisableParallelization = true)]
public class LookCollection { }

/// <summary>The classic look is the default; the instrument look is opt-in and swaps every classic style it replaces.</summary>
[Collection("Look")]
public class InstrumentLookTests
{
    [Fact]
    public void Classic_look_is_the_default()
    {
        Assert.False(new AppSettings().InstrumentLook);
        WithWindow(false, window =>
        {
            var icon = Assert.IsType<TextBlock>(((Button)window.FindName("NotesToolBtn")).Content);
            Assert.Equal("", icon.Text);
        });
    }

    [Fact]
    public void Instrument_look_swaps_the_styles_and_names_the_tools_with_words()
        => WithWindow(true, window =>
        {
            var notes = (Button)window.FindName("NotesToolBtn");
            Assert.Equal("Notes", Assert.IsType<TextBlock>(notes.Content).Text);
            Assert.Same(window.Resources["ToolButton"], notes.Style);
            var pill = (Style)window.FindResource("CountPill");
            Assert.Contains(pill.Setters.OfType<Setter>(), s => s.Property == Border.CornerRadiusProperty && Equals(s.Value, new CornerRadius(3)));
            Assert.Equal(new CornerRadius(4), ((System.Windows.Controls.Border)window.FindName("BigArt")).CornerRadius);

            var toolBar = (FrameworkElement)window.FindName("ToolBar");
            typeof(MainWindow).GetMethod("PlaceToolBar", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, new object[] { true });
            Assert.Equal(1, Grid.GetRow(toolBar));
            Assert.Equal(HorizontalAlignment.Left, toolBar.HorizontalAlignment);
        });

    static void WithWindow(bool instrument, Action<MainWindow> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            bool was = Look.Instrument;
            MainWindow? window = null;
            try
            {
                Look.Instrument = instrument;
                window = new MainWindow();
                test(window);
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); Look.Instrument = was; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The window did not finish.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
