import 'dart:math' as math;

import 'package:flutter/widgets.dart';

/// A little mole for Mullvad VPN ("mullvad" is Swedish for mole): it pops out of its hole when the tunnel comes up,
/// looks around and gives a nod; when the tunnel goes down it ducks back in. Our own drawing, not Mullvad's logo.
class Mole extends StatefulWidget {
  final bool up;
  final double size;
  const Mole({super.key, required this.up, this.size = 28});

  @override
  State<Mole> createState() => _MoleState();
}

class _MoleState extends State<Mole> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: 1600))..forward();

  @override
  void didUpdateWidget(Mole old) {
    super.didUpdateWidget(old);
    if (old.up != widget.up) _c.forward(from: 0);
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
        animation: _c,
        builder: (context, _) => CustomPaint(size: Size.square(widget.size), painter: _MolePainter(_c.value, widget.up)),
      );
}

class _MolePainter extends CustomPainter {
  final double t;
  final bool up;
  _MolePainter(this.t, this.up);

  static const fur = Color(0xFFB07A4A), dark = Color(0xFF5A3A22), nose = Color(0xFFFF8FA3), dirt = Color(0xFF3B2A1C);

  /// How far out of the hole (0 in, 1 out): a springy pop when coming up, a quick duck when going down.
  double get rise {
    if (!up) return 1 - Curves.easeInBack.transform((t * 2).clamp(0.0, 1.0));
    final p = (t * 1.6).clamp(0.0, 1.0);
    return Curves.elasticOut.transform(p);
  }

  @override
  void paint(Canvas canvas, Size size) {
    final s = size.width;
    final ground = s * 0.86;
    canvas.save();
    // Everything above the ground (the mole is hidden below it, in its hole).
    canvas.clipRect(Rect.fromLTRB(-s, -s, s * 2, ground + 0.5));
    final top = ground - s * 0.78 * rise;
    // Looking left and right once it's out, then a nod.
    final look = up && t > 0.55 ? math.sin((t - 0.55) / 0.45 * math.pi * 2) * s * 0.06 : 0.0;
    final body = RRect.fromRectAndCorners(Rect.fromLTWH(s * 0.2, top, s * 0.6, s * 0.95),
        topLeft: Radius.circular(s * 0.3), topRight: Radius.circular(s * 0.3));
    canvas.drawRRect(body, Paint()..color = fur);
    // Belly, eyes, nose, whiskers.
    canvas.drawOval(Rect.fromLTWH(s * 0.33, top + s * 0.38, s * 0.34, s * 0.45), Paint()..color = const Color(0xFFD9AE84));
    final eye = Paint()..color = dark;
    canvas.drawCircle(Offset(s * 0.4 + look, top + s * 0.2), s * 0.035, eye);
    canvas.drawCircle(Offset(s * 0.6 + look, top + s * 0.2), s * 0.035, eye);
    canvas.drawOval(Rect.fromCenter(center: Offset(s * 0.5 + look, top + s * 0.31), width: s * 0.12, height: s * 0.08), Paint()..color = nose);
    final whisker = Paint()
      ..color = dark.withValues(alpha: 0.7)
      ..strokeWidth = s * 0.018;
    for (final dy in [-0.02, 0.03]) {
      canvas.drawLine(Offset(s * 0.42 + look, top + s * (0.32 + dy)), Offset(s * 0.27 + look, top + s * (0.3 + dy * 2)), whisker);
      canvas.drawLine(Offset(s * 0.58 + look, top + s * (0.32 + dy)), Offset(s * 0.73 + look, top + s * (0.3 + dy * 2)), whisker);
    }
    // Little paws on the rim.
    if (rise > 0.6) {
      final paw = Paint()..color = const Color(0xFFE9C9A6);
      canvas.drawOval(Rect.fromLTWH(s * 0.22, ground - s * 0.1, s * 0.16, s * 0.1), paw);
      canvas.drawOval(Rect.fromLTWH(s * 0.62, ground - s * 0.1, s * 0.16, s * 0.1), paw);
    }
    canvas.restore();
    // The hole.
    canvas.drawOval(Rect.fromCenter(center: Offset(s * 0.5, ground), width: s * 0.9, height: s * 0.2), Paint()..color = dirt);
  }

  @override
  bool shouldRepaint(_MolePainter old) => old.t != t || old.up != up;
}
