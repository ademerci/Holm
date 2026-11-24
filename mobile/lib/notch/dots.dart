import 'package:flutter/widgets.dart';

/// Dot-matrix lettering for the Nothing look of the open notch, drawn dot by dot (5×7 glyphs), in the spirit of
/// Nothing's dotted type without using its font.
class DotText extends StatelessWidget {
  final String text;
  final double dot; // the size of one dot cell
  final Color color;
  final int maxChars;
  const DotText(this.text, {super.key, this.dot = 3, this.color = const Color(0xFFFFFFFF), this.maxChars = 40});

  @override
  Widget build(BuildContext context) {
    final t = text.toUpperCase().characters.take(maxChars).toString();
    return CustomPaint(
      size: Size(_DotPainter.widthOf(t, dot), dot * 7),
      painter: _DotPainter(t, dot, color),
    );
  }
}

class _DotPainter extends CustomPainter {
  final String text;
  final double dot;
  final Color color;
  _DotPainter(this.text, this.dot, this.color);

  static double widthOf(String text, double dot) => text.isEmpty ? 0 : (text.length * 6 - 1) * dot;

  @override
  void paint(Canvas canvas, Size size) {
    final on = Paint()..color = color;
    final off = Paint()..color = color.withValues(alpha: 0.08);
    final r = dot * 0.38;
    var x0 = 0.0;
    for (final ch in text.split('')) {
      final rows = _glyphs[ch] ?? _glyphs['?']!;
      for (var row = 0; row < 7; row++) {
        for (var col = 0; col < 5; col++) {
          final lit = rows[row] & (1 << (4 - col)) != 0;
          canvas.drawCircle(Offset(x0 + col * dot + dot / 2, row * dot + dot / 2), r, lit ? on : off);
        }
      }
      x0 += dot * 6;
    }
  }

  @override
  bool shouldRepaint(_DotPainter old) => old.text != text || old.color != color || old.dot != dot;

  static const _glyphs = <String, List<int>>{
    ' ': [0, 0, 0, 0, 0, 0, 0],
    '0': [14, 17, 19, 21, 25, 17, 14],
    '1': [4, 12, 4, 4, 4, 4, 14],
    '2': [14, 17, 1, 2, 4, 8, 31],
    '3': [31, 2, 4, 2, 1, 17, 14],
    '4': [2, 6, 10, 18, 31, 2, 2],
    '5': [31, 16, 30, 1, 1, 17, 14],
    '6': [6, 8, 16, 30, 17, 17, 14],
    '7': [31, 1, 2, 4, 8, 8, 8],
    '8': [14, 17, 17, 14, 17, 17, 14],
    '9': [14, 17, 17, 15, 1, 2, 12],
    'A': [14, 17, 17, 31, 17, 17, 17],
    'B': [30, 17, 17, 30, 17, 17, 30],
    'C': [14, 17, 16, 16, 16, 17, 14],
    'D': [28, 18, 17, 17, 17, 18, 28],
    'E': [31, 16, 16, 30, 16, 16, 31],
    'F': [31, 16, 16, 30, 16, 16, 16],
    'G': [14, 17, 16, 23, 17, 17, 15],
    'H': [17, 17, 17, 31, 17, 17, 17],
    'I': [14, 4, 4, 4, 4, 4, 14],
    'J': [7, 2, 2, 2, 2, 18, 12],
    'K': [17, 18, 20, 24, 20, 18, 17],
    'L': [16, 16, 16, 16, 16, 16, 31],
    'M': [17, 27, 21, 21, 17, 17, 17],
    'N': [17, 17, 25, 21, 19, 17, 17],
    'O': [14, 17, 17, 17, 17, 17, 14],
    'P': [30, 17, 17, 30, 16, 16, 16],
    'Q': [14, 17, 17, 17, 21, 18, 13],
    'R': [30, 17, 17, 30, 20, 18, 17],
    'S': [15, 16, 16, 14, 1, 1, 30],
    'T': [31, 4, 4, 4, 4, 4, 4],
    'U': [17, 17, 17, 17, 17, 17, 14],
    'V': [17, 17, 17, 17, 17, 10, 4],
    'W': [17, 17, 17, 21, 21, 21, 10],
    'X': [17, 17, 10, 4, 10, 17, 17],
    'Y': [17, 17, 17, 10, 4, 4, 4],
    'Z': [31, 1, 2, 4, 8, 16, 31],
    'Ä': [10, 0, 14, 17, 31, 17, 17],
    'Ö': [10, 0, 14, 17, 17, 17, 14],
    'Ü': [10, 0, 17, 17, 17, 17, 14],
    ':': [0, 12, 12, 0, 12, 12, 0],
    '.': [0, 0, 0, 0, 0, 12, 12],
    ',': [0, 0, 0, 0, 12, 4, 8],
    '-': [0, 0, 0, 31, 0, 0, 0],
    '+': [0, 4, 4, 31, 4, 4, 0],
    '%': [24, 25, 2, 4, 8, 19, 3],
    '/': [0, 1, 2, 4, 8, 16, 0],
    "'": [4, 4, 8, 0, 0, 0, 0],
    '!': [4, 4, 4, 4, 4, 0, 4],
    '?': [14, 17, 1, 2, 4, 0, 4],
    '&': [12, 18, 20, 8, 21, 18, 13],
    '(': [2, 4, 8, 8, 8, 4, 2],
    ')': [8, 4, 2, 2, 2, 4, 8],
    '·': [0, 0, 0, 4, 0, 0, 0],
  };
}
