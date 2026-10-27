using System.Text.Json;

namespace WinNotch.Tests;

public class CalculatorTests
{
    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("10/4", 2.5)]
    [InlineData("2^10", 1024)]
    [InlineData("2^3^2", 512)]          // right to left
    [InlineData("-2^2", -4)]            // as on paper
    [InlineData("(-2)^2", 4)]
    [InlineData("--3", 3)]
    [InlineData("200*15%", 30)]
    [InlineData("50 + 15%", 57.5)]      // adds 15 % of 50, as on any calculator
    [InlineData("80 - 25%", 60)]
    [InlineData("12,5 × 4 + 15%", 57.5)]
    [InlineData("50 + 2*15%", 50.3)]    // not a bare percentage: plain 0.3
    [InlineData("50 + (15%)", 50.15)]
    [InlineData("15%", 0.15)]
    [InlineData("50 + 10%^2", 50.01)]
    [InlineData("1,5 × 2", 3)]          // decimal comma, times sign
    [InlineData("9 ÷ 3", 3)]
    [InlineData("7 − 2", 5)]
    [InlineData("2(3+4)", 14)]          // implicit multiplication
    [InlineData("sqrt(16)+1", 5)]
    [InlineData("sqrt 2 ^ 2", 2)]
    [InlineData("abs(-3)", 3)]
    [InlineData("log(1000)", 3)]
    [InlineData("1 000 000 / 4", 250000)]
    [InlineData("1e3+1", 1001)]
    [InlineData("3 * 4 =", 12)]         // a trailing equals sign is fine
    [InlineData(".5+.25", 0.75)]
    public void Calculates(string input, double expected)
    {
        double? result = Calculator.Evaluate(input);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value, 9);
    }

    [Fact]
    public void Knows_pi_and_e()
    {
        Assert.Equal(Math.PI * 2, Calculator.Evaluate("2pi")!.Value, 12);
        Assert.Equal(1, Calculator.Evaluate("ln(e)")!.Value, 12);
        Assert.Equal(0, Calculator.Evaluate("sin(pi)")!.Value, 12);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1+")]
    [InlineData("(1+2")]
    [InlineData("1/0")]
    [InlineData("2*/3")]
    [InlineData("hello")]
    [InlineData("Process.Start(\"calc\")")] // never code, just a calculation that isn't one
    [InlineData("sqrt(-1)")]
    [InlineData("10^400")]
    public void Refuses_what_isnt_a_complete_calculation(string input)
        => Assert.Null(Calculator.Evaluate(input));

    [Theory]
    [InlineData(42, "42")]
    [InlineData(2.5, "2.5")]
    [InlineData(1.0 / 3, "0.333333333333")]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(-0.0, "0")]
    [InlineData(1e-17, "0")]
    [InlineData(123456789012345.0, "1.23456789012E+14")]
    public void Formats_without_noise(double value, string expected) => Assert.Equal(expected, Calculator.Format(value));

    [Fact]
    public void Settings_from_before_the_calculator_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Animations\":false,\"Notes\":false}")!;
        Assert.False(old.Notes);
        Assert.True(old.Calculator);
    }
}
