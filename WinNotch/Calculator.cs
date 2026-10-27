using System;
using System.Globalization;

namespace WinNotch;

/// <summary>
/// A small calculator for the open notch: + − × ÷ ^ %, brackets, decimal point or comma, and sqrt, sin, cos, tan,
/// ln, log, abs with pi and e. Its own little parser (recursive descent), so nothing typed is ever run as code.
/// </summary>
internal static class Calculator
{
    /// <summary>The result, or null when the input isn't a complete calculation (or divides by zero).</summary>
    public static double? Evaluate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 300) return null;
        var parser = new Parser(Normalize(input));
        try
        {
            double value = parser.Expression();
            if (!parser.AtEnd || double.IsNaN(value) || double.IsInfinity(value)) return null;
            return value;
        }
        catch (FormatException) { return null; }
    }

    /// <summary>The symbols people type instead of + − * /, and the comma as a decimal point ("1,5 × 2").</summary>
    static string Normalize(string input) => input
        .Replace('×', '*').Replace('·', '*').Replace('÷', '/').Replace('−', '-').Replace('–', '-').Replace(':', '/')
        .Replace(',', '.').Replace('\'', ' ').Trim().TrimEnd('=').Trim();

    /// <summary>"42", "0.333333333333", "1.5e+21": at most 12 significant digits, no trailing zeros.</summary>
    public static string Format(double value)
    {
        if (Math.Abs(value) < 1e-12) value = 0; // no "-0" or "1E-17" from rounding
        return value.ToString("G12", CultureInfo.InvariantCulture);
    }

    sealed class Parser
    {
        readonly string _s;
        int _i;

        public Parser(string s) => _s = s;

        public bool AtEnd { get { Skip(); return _i >= _s.Length; } }

        void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

        bool Take(char c)
        {
            Skip();
            if (_i < _s.Length && _s[_i] == c) { _i++; return true; }
            return false;
        }

        bool _percent;     // the last postfix ended in '%'
        bool _termPercent; // the last term was nothing but a percentage ("15%")

        // expression := term (('+' | '-') term)*    "50 + 15%" adds 15 % of 50, as on any calculator
        public double Expression()
        {
            double value = Term();
            while (true)
            {
                if (Take('+')) { double t = Term(); value += _termPercent ? value * t : t; }
                else if (Take('-')) { double t = Term(); value -= _termPercent ? value * t : t; }
                else return value;
            }
        }

        // term := unary (('*' | '/' | implicit) unary)*      "2(3+4)" and "2pi" multiply
        double Term()
        {
            _percent = false;
            double value = Unary();
            _termPercent = _percent;
            while (true)
            {
                if (Take('*')) { _termPercent = false; value *= Unary(); }
                else if (Take('/'))
                {
                    _termPercent = false;
                    double by = Unary();
                    if (by == 0) throw new FormatException("Division by zero");
                    value /= by;
                }
                else if (StartsFactor()) { _termPercent = false; value *= Unary(); }
                else return value;
            }
        }

        bool StartsFactor()
        {
            Skip();
            return _i < _s.Length && (_s[_i] == '(' || char.IsLetter(_s[_i]));
        }

        // unary := ('-' | '+') unary | power        so -2^2 is -(2^2), as on paper
        double Unary()
        {
            if (Take('-')) return -Unary();
            if (Take('+')) return Unary();
            return Power();
        }

        // power := postfix ('^' unary)?             right to left: 2^3^2 = 2^9
        double Power()
        {
            double value = Postfix();
            if (!Take('^')) return value;
            value = Math.Pow(value, Unary());
            _percent = false;
            return value;
        }

        // postfix := primary '%'*                   "15%" is 0.15
        double Postfix()
        {
            double value = Primary();
            _percent = false; // a bracket's inside doesn't count: "(15%)" is still a percentage only if it ends in '%'
            while (Take('%')) { value /= 100; _percent = true; }
            return value;
        }

        double Primary()
        {
            Skip();
            if (Take('('))
            {
                double inner = Expression();
                if (!Take(')')) throw new FormatException("Missing )");
                return inner;
            }
            if (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) return Number();
            if (_i < _s.Length && char.IsLetter(_s[_i])) return Name();
            throw new FormatException("Expected a number");
        }

        double Number()
        {
            int start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == ' ' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1]) && char.IsDigit(_s[_i - 1])))
                _i++;
            if (_i < _s.Length && (_s[_i] is 'e' or 'E') && _i + 1 < _s.Length && (char.IsDigit(_s[_i + 1]) || _s[_i + 1] is '-' or '+'))
            {
                _i += 2;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
            }
            string text = _s[start.._i].Replace(" ", ""); // "1 000 000" is a million
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) throw new FormatException("Bad number");
            return value;
        }

        double Name()
        {
            int start = _i;
            while (_i < _s.Length && char.IsLetter(_s[_i])) _i++;
            string name = _s[start.._i].ToLowerInvariant();
            switch (name)
            {
                case "pi" or "π": return Math.PI;
                case "e": return Math.E;
            }
            Func<double, double>? f = name switch
            {
                "sqrt" or "wurzel" => Math.Sqrt,
                "sin" => x => Math.Sin(x),
                "cos" => x => Math.Cos(x),
                "tan" => x => Math.Tan(x),
                "ln" => Math.Log,
                "log" => Math.Log10,
                "abs" => Math.Abs,
                _ => null,
            };
            if (f == null) throw new FormatException("Unknown name " + name);
            return f(Unary());
        }
    }
}
