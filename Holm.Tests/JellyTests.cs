using WinNotch;
using Xunit;

namespace Holm.Tests;

// Spring.Jelly is static: these run one after another, never beside other spring tests.
[Collection("Spring")]
public class JellyTests
{
    [Fact]
    public void Jelly_wobbles_far_past_the_target_and_still_comes_to_rest()
    {
        try
        {
            static (double Peak, double Seconds) Run()
            {
                var s = new SpringValue(0);
                s.Retarget(100, 0.36, 0.64);
                double peak = 0, t = 0;
                while (!s.Step(1 / 60.0) && t < 20) { t += 1 / 60.0; peak = System.Math.Max(peak, s.Value); }
                return (peak, t);
            }
            Spring.Jelly = false;
            var normal = Run();
            Spring.Jelly = true;
            var jelly = Run();
            Assert.True(jelly.Peak > 150, $"jelly peak {jelly.Peak}");    // way over the top
            Assert.True(jelly.Peak > normal.Peak + 30);
            Assert.True(jelly.Seconds < 10, $"jelly took {jelly.Seconds} s"); // but it does stop
            Assert.Equal(1, Spring.Smooth.Ease(1));                           // closing still glides
        }
        finally { Spring.Jelly = false; }
    }

    [Fact]
    public void Jelly_is_off_by_default() => Assert.False(new AppSettings().Jelly);
}
