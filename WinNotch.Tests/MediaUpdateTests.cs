using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinNotch.Tests;

/// <summary>Spotify re-sends the same state every ~4.5 s, also while paused. None of that may cost a redraw.</summary>
public class MediaUpdateTests
{
    [Fact]
    public void PausedTimelineAgainIsTheSame()
    {
        var shown = Paused();
        var again = Paused();
        again.PositionStamp = shown.PositionStamp.AddSeconds(4.5); // Spotify only moves the stamp

        Assert.True(shown.SameAs(again));
        Assert.True(shown.SameTimeline(again));
    }

    [Fact]
    public void PlayingPositionCountsFromItsStamp()
    {
        var now = DateTimeOffset.Now;
        var shown = Paused();
        shown.IsPlaying = true;
        shown.Position = TimeSpan.FromSeconds(10);
        shown.PositionStamp = now.AddSeconds(-5);

        var onTime = Paused();
        onTime.IsPlaying = true;
        onTime.Position = TimeSpan.FromSeconds(15.2);
        onTime.PositionStamp = now;
        Assert.True(shown.SameAs(onTime));

        var skipped = Paused();
        skipped.IsPlaying = true;
        skipped.Position = TimeSpan.FromSeconds(40);
        skipped.PositionStamp = now;
        Assert.False(shown.SameAs(skipped));
    }

    static readonly Dictionary<string, Action<MediaInfo>> Changes = new()
    {
        ["title"] = i => i.Title = "Other song",
        ["artist"] = i => i.Artist = "Someone else",
        ["album"] = i => i.Album = "Other album",
        ["app"] = i => i.App = "Chrome",
        ["playing"] = i => i.IsPlaying = true,
        ["shuffle"] = i => i.Shuffle = true,
        ["repeat"] = i => i.Repeat = 1,
        ["can next"] = i => i.CanNext = false,
        ["can seek"] = i => i.CanSeek = false,
        ["cover"] = i => i.Art = Bitmap(),
        ["backdrop"] = i => i.Backdrop = Bitmap(),
        ["accent"] = i => i.Accent = Colors.Orange,
        ["shade"] = i => i.BackdropShade = 0.7,
        ["seeked"] = i => i.Position += TimeSpan.FromSeconds(2),
        ["length"] = i => i.Duration += TimeSpan.FromSeconds(1),
    };

    public static IEnumerable<object[]> ChangeNames => Changes.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ChangeNames))]
    public void EveryVisibleChangeCounts(string what)
    {
        var shown = Paused();
        var next = Paused();
        Changes[what](next);
        Assert.False(shown.SameAs(next), what);
    }

    [Fact]
    public void RepeatedUpdatesAreNotPublished()
    {
        OnSta(() =>
        {
            var media = new MediaService(Dispatcher.CurrentDispatcher);
            var seen = new List<MediaInfo?>();
            media.Changed += (info, _) => seen.Add(info);
            var publish = typeof(MediaService).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!;

            var first = Paused();
            publish.Invoke(media, new object?[] { first, true });
            for (int i = 1; i <= 10; i++)
            {
                var again = Paused();
                again.PositionStamp = first.PositionStamp.AddSeconds(4.5 * i);
                publish.Invoke(media, new object?[] { again, false });
            }
            Assert.Equal(new[] { first }, seen);

            var playing = Paused();
            playing.IsPlaying = true;
            publish.Invoke(media, new object?[] { playing, false });
            publish.Invoke(media, new object?[] { null, false });
            publish.Invoke(media, new object?[] { null, false });
            Assert.Equal(new[] { first, playing, null }, seen);

            // The same song starting over is news, even when it looks the same.
            publish.Invoke(media, new object?[] { Paused(), true });
            Assert.Equal(4, seen.Count);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameCoverKeepsItsBrushes(bool expanded)
    {
        OnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                // Never shown, so no real service starts; settings only change in memory.
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                ((AppSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!).Animations = false;
                typeof(MainWindow).GetField("_expanded", flags)!.SetValue(window, expanded);
                var changed = typeof(MainWindow).GetMethod("OnMediaChanged", flags)!;
                var compact = (Border)window.FindName("CompactArt");
                var big = (Border)window.FindName("BigArt");
                var backdropA = (Border)window.FindName("ArtLayerA");
                var backdropB = (Border)window.FindName("ArtLayerB");
                var bar = (Rectangle)window.FindName("Bar1");

                var info = Paused();
                info.IsPlaying = true; // the backdrop only shows while playing or open
                changed.Invoke(window, new object?[] { info, true });
                var before = (compact.Background, big.Background, backdropA.Background, backdropB.Background, bar.Fill);
                Assert.IsType<ImageBrush>(compact.Background);

                // A seek is a real update, but the cover, the backdrop and the colour stay what they were.
                var seeked = Paused();
                seeked.IsPlaying = true;
                seeked.Art = info.Art;
                seeked.Backdrop = info.Backdrop;
                seeked.Position += TimeSpan.FromSeconds(30);
                changed.Invoke(window, new object?[] { seeked, false });

                Assert.Same(before.Item1, compact.Background);
                Assert.Same(before.Item2, big.Background);
                Assert.Same(before.Item3, backdropA.Background);
                Assert.Same(before.Item4, backdropB.Background);
                Assert.Same(before.Item5, bar.Fill);
                if (expanded) Assert.Same(compact.Background, big.Background);
            }
            finally { window.Close(); }
        });
    }

    static readonly ImageSource Cover = Bitmap(), Wash = Bitmap();

    /// <summary>Spotify, paused at 0:42 of a 3:20 song.</summary>
    static MediaInfo Paused() => new()
    {
        Title = "Paradise", Artist = "dentist", Album = "Paradise", App = "Spotify", AppId = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
        CanPlayPause = true, CanNext = true, CanPrevious = true, CanSeek = true, CanShuffle = true, CanRepeat = true,
        Shuffle = false, Repeat = 0, Art = Cover, Backdrop = Wash, Accent = Colors.HotPink, BackdropShade = 0.5,
        Duration = TimeSpan.FromSeconds(200), Position = TimeSpan.FromSeconds(42), PositionStamp = DateTimeOffset.Now,
    };

    static BitmapSource Bitmap()
    {
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        bitmap.Freeze();
        return bitmap;
    }

    static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The media check did not finish.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
