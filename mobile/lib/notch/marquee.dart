import 'package:flutter/scheduler.dart';
import 'package:material_ui/material_ui.dart';

/// One line of text that shows all of itself: when it doesn't fit, it rests for [pause], slides to its end, rests
/// again and slides back, over and over. Text that fits just stands, like a plain [Text].
class Marquee extends StatefulWidget {
  final String text;
  final TextStyle? style;
  final Duration pause;

  /// How fast it slides, in logical pixels a second.
  final double speed;

  const Marquee(this.text, {super.key, this.style, this.pause = const Duration(milliseconds: 1500), this.speed = 32});

  @override
  State<Marquee> createState() => _MarqueeState();
}

class _MarqueeState extends State<Marquee> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this);
  double _overflow = 0;

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  /// One round: rest, to the end, rest, back to the start.
  Duration _round(double overflow) {
    final slide = overflow / widget.speed;
    return widget.pause * 2 + Duration(milliseconds: (slide * 2 * 1000).round());
  }

  /// Where the text is at [t] of a round: 0 at its start, 1 at its end.
  double _at(double t, double overflow) {
    final total = _round(overflow).inMilliseconds.toDouble();
    final pause = widget.pause.inMilliseconds.toDouble();
    final slide = (total - 2 * pause) / 2;
    final ms = t * total;
    if (ms < pause) return 0;
    if (ms < pause + slide) return Curves.easeInOut.transform((ms - pause) / slide);
    if (ms < 2 * pause + slide) return 1;
    return 1 - Curves.easeInOut.transform(((ms - 2 * pause - slide) / slide).clamp(0.0, 1.0));
  }

  void _follow(double overflow) {
    final moves = overflow > 0.5;
    if ((overflow - _overflow).abs() < 1 && moves == _c.isAnimating) return;
    _overflow = overflow;
    SchedulerBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      if (moves) {
        _c.duration = _round(overflow);
        _c.repeat(); // starts again from the start, resting first
      } else {
        _c.stop();
        _c.value = 0;
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final style = DefaultTextStyle.of(context).style.merge(widget.style);
    return LayoutBuilder(
      builder: (context, box) {
        final painter = TextPainter(
          text: TextSpan(text: widget.text, style: style),
          maxLines: 1,
          textDirection: Directionality.of(context),
          textScaler: MediaQuery.textScalerOf(context),
        )..layout();
        final width = box.maxWidth.isFinite ? box.maxWidth : painter.width;
        final overflow = painter.width - width;
        _follow(overflow);
        final line = Text(widget.text, maxLines: 1, softWrap: false, style: widget.style);
        if (overflow <= 0.5) return line;
        return SizedBox(
          width: width,
          height: painter.height,
          child: ClipRect(
            child: AnimatedBuilder(
              animation: _c,
              builder: (context, child) => Transform.translate(offset: Offset(-overflow * _at(_c.value, overflow), 0), child: child),
              child: OverflowBox(alignment: Alignment.centerLeft, minWidth: 0, maxWidth: double.infinity, child: line),
            ),
          ),
        );
      },
    );
  }
}
