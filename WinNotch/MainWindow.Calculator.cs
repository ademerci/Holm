using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinNotch;

/// <summary>
/// A quick calculator in the open notch: the result shows while you type, and a click on it (or Enter) copies it.
/// What you type is read by <see cref="Calculator"/>'s own parser, never run as code.
/// </summary>
public partial class MainWindow
{
    const string CalculatorGlyph = "";
    string? _calcResult;

    void InitCalculator() => AllowTyping(CalcBox);

    void CalcBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        CalcPlaceholder.Visibility = CalcBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _calcResult = Calculator.Evaluate(CalcBox.Text) is { } value ? Calculator.Format(value) : null;
        CalcResult.Text = _calcResult == null ? (CalcBox.Text.Trim().Length == 0 ? "" : "…") : "= " + _calcResult;
        CalcHint.Text = _calcResult == null ? "+ − × ÷ ^ % ( ) · sqrt, sin, ln, pi" : "Click or Enter to copy";
    }

    void CalcBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CopyCalcResult();
    }

    void CalcResult_Click(object sender, RoutedEventArgs e) => CopyCalcResult();

    void CopyCalcResult()
    {
        if (_calcResult == null) return;
        CalcHint.Text = TrySetClipboard(_calcResult) ? $"{_calcResult} copied" : "Clipboard busy, try again";
    }

    void ShowCalculator() => CalcBox_TextChanged(CalcBox, null!);

    void CalculatorTool_Click(object sender, RoutedEventArgs e) => ToggleTool(Tool.Calculator);

    void MenuCalculator_Click(object sender, RoutedEventArgs e)
    {
        _settings.Calculator = MenuCalculator.IsChecked;
        _settings.Save();
        RefreshToolBar();
    }
}
