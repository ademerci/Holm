import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/scheduler.dart';
import 'package:flutter/services.dart';
import 'package:material_ui/material_ui.dart';

import 'activities.dart';
import 'brands.dart';
import 'cover.dart';
import 'designs.dart';
import 'marquee.dart';
import 'dots.dart';
import 'mole.dart';
import 'settings.dart';
import 'spring.dart';

const _nothingRed = Color(0xFFD71921);
const _dim = Color(0x99FFFFFF);
const _faint = Color(0x66FFFFFF);

/// How tall the open notch is for what it shows: as much as its content needs, at most the height set for it. A timer
/// stays low, a player takes its room, a notification as many lines as its text has.
double openHeight(NotchSettings s, NotchActivity a, NotchActivity? second, [NotchController? controller]) {
  final c = controller ?? _noController;
  final band = (s.closed.y - s.open.y + s.closed.height + 4).clamp(28.0, 80.0);
  final actions = a.actions.isEmpty && a.replyIndex == null ? 0.0 : 54.0;
  double lines(String? text, int max) => text == null ? 0 : 20.0 * (text.length / 34).ceil().clamp(1, max);
  final content = switch (a.kind) {
    ActivityKind.music => 150.0,
    ActivityKind.timer => 66.0 + actions,
    ActivityKind.charging => 64.0,
    ActivityKind.vpn => a.mullvad ? 70.0 : 56.0,
    ActivityKind.wifi || ActivityKind.bluetooth || ActivityKind.privacy => 56.0,
    ActivityKind.volume => 5 * 32.0,
    ActivityKind.call => 112.0,
    ActivityKind.navigation => 72.0,
    ActivityKind.home => 132.0,
    ActivityKind.buds => 120.0,
    ActivityKind.screenshot => 118.0,
    ActivityKind.clipboard => 26 + lines(a.text, 3) + 54,
    _ when a.isOffer => 108.0,
    _ => math.max(48.0, 26 + lines(a.text, a.hasProgress ? 1 : 3) + (a.hasProgress && !a.done ? 22 : 0)) + actions,
  };
  final h = band + content + _inset + (second != null ? 36 : 0) + (a.replyIndex != null && c.typing ? 8 : 0);
  return math.min(s.open.height, h);
}

final _noController = NotchController();

/// A notification banner's icon, and its corner: the icon sits concentric in the rounded corner, so its margin to the
/// side and to the bottom is the corner's radius less its own.
const _bannerIcon = 34.0;
double _bannerRadius(NotchSettings s) => math.max(_bannerIcon / 2 + 10, math.min(s.open.radius, 40.0));
double _bannerInset(double radius) => radius - _bannerIcon / 2;

/// The design of the open notch (Holm's look only; Nothing's has its own).
NotchDesign? _design(NotchController c) => c.settings.look == NotchLook.holm ? NotchDesign.byId(c.settings.design) : null;

/// The open notch's margin, the same at the sides and at the bottom.
const _inset = 20.0;

/// Buttons in the open notch are rounded like its corners, less the margin, so the curves run alongside.
double _buttonRadius(NotchSettings s) => math.max(12.0, s.open.radius - _inset);

/// The notch: black around the camera while closed, the open card when tapped, moved by the desktop's springs.
/// [onWindow] (overlay only) learns how big the window must be: the notch's bounds while anything shows, else nothing.
class NotchStage extends StatefulWidget {
  final NotchController controller;

  /// [rect] is where touches count (left, top, width, height in the window), when the overlay can limit them.
  final void Function(double width, double height, bool touchable, List<double>? rect)? onWindow;
  const NotchStage(this.controller, {super.key, this.onWindow});

  @override
  State<NotchStage> createState() => _NotchStageState();
}

class _NotchStageState extends State<NotchStage> with SingleTickerProviderStateMixin {
  final _shape = ShapeSprings();
  final _bubble = Spring(0), _bubbleAlpha = Spring(0, positionTolerance: 0.004, velocityTolerance: 0.02);
  late final Ticker _ticker = createTicker(_onTick);
  Duration _last = Duration.zero;
  NotchGeometry _target = const NotchSettings().closed;
  Size _window = Size.zero;
  List<double>? _rect;
  /// How tall the notch is when open now (it follows what it shows), for the corners on the way.
  double _openHeight = const NotchSettings().open.height;

  NotchController get c => widget.controller;

  @override
  void initState() {
    super.initState();
    final g = _goal().$1;
    _shape.snapTo(g.height, g.height, g.radius, g.x, g.y, 0);
    c.addListener(_retarget);
    _retarget();
  }

  @override
  void didUpdateWidget(NotchStage old) {
    super.didUpdateWidget(old);
    if (old.controller != c) {
      old.controller.removeListener(_retarget);
      c.addListener(_retarget);
    }
  }

  @override
  void dispose() {
    c.removeListener(_retarget);
    _ticker.dispose();
    super.dispose();
  }

  /// Where the notch is going: hidden (shrunk onto the camera, faded), closed, peeking with an alert, or open.
  (NotchGeometry, double) _goal() {
    final s = c.settings;
    final p = c.primary;
    final closed = s.closed;
    if (p == null) return (closed.copyWith(width: closed.height), 0); // over the camera, invisible; a tap opens the overview
    if (c.expanded) {
      final v = c.visible;
      return (s.open.copyWith(height: openHeight(s, p, v.length > 1 ? v[1] : null, c)), 1);
    }
    if (p.wide) return (_wideShape(s), 1);
    if (p.banner) return (s.compactBanners ? _compactShape(s) : _bannerShape(s), 1);
    return (closed, 1);
  }

  /// Short news: the closed pill, wider (a little taller), still round at the ends.
  static NotchGeometry _wideShape(NotchSettings s) {
    final h = s.closed.height + 4;
    return s.closed.copyWith(width: math.min(s.open.width, s.closed.width + 150), height: h, radius: h / 2);
  }

  /// A compact notification: as tall as the closed notch, a little wider than the pill for short news (the message
  /// needs a few more words than "Connected").
  static NotchGeometry _compactShape(NotchSettings s) =>
      s.closed.copyWith(width: math.min(s.open.width - 28, s.closed.width + 120), radius: s.closed.height / 2);

  /// A notification: a banner as wide as the open notch, the camera's band on top, the message under it.
  static NotchGeometry _bannerShape(NotchSettings s) {
    final r = _bannerRadius(s);
    // The icon with the same margin above, below and beside it; the camera fits in beside the text.
    final h = math.max(s.closed.height + 8, _bannerIcon + 2 * _bannerInset(r));
    return s.closed.copyWith(width: s.open.width - 28, height: h, radius: r);
  }

  void _retarget() {
    final (g, alpha) = _goal();
    final grow = g.width * g.height > _shape.w.value * _shape.h.value;
    _target = g;
    if (c.expanded) _openHeight = g.height;
    _shape.moveTo(g.width, g.height, g.radius, g.x, g.y, alpha, grow: grow);
    // The bubble only beside the closed notch: a banner or widened pill would push it off the screen. The earbuds
    // have it whenever they're connected, also beside the bare camera.
    final two = (c.budsBubble != null || c.visible.length > 1) && !c.expanded && !(c.primary?.peeks ?? false);
    _bubble.retarget(two ? c.settings.closed.height : 0, 0.34, 0.66);
    _bubbleAlpha.retarget(two ? 1 : 0, 0.24, 1);
    _reportWindow();
    if (!_ticker.isActive) {
      _last = Duration.zero;
      _ticker.start();
    }
    setState(() {});
  }

  void _onTick(Duration now) {
    final dt = _last == Duration.zero ? 1 / 60 : (now - _last).inMicroseconds / 1e6;
    _last = now;
    final a = _shape.step(math.min(dt, 1 / 20));
    final b = _bubble.step(math.min(dt, 1 / 20));
    final o = _bubbleAlpha.step(math.min(dt, 1 / 20));
    if (a && b && o) {
      _ticker.stop();
      _reportWindow();
    } else if (c.touchRegion) {
      _reportWindow(); // the touch area follows the notch as it moves
    }
    setState(() {});
  }

  /// The window keeps one width while the notch shows (as wide as it ever gets): when a window changes width, Android
  /// shows the last frame in the new window for a moment, and the notch seemed to jump sideways. Only the height
  /// follows the notch, growing before it opens and shrinking once it has closed.
  void _reportWindow() {
    final report = widget.onWindow;
    if (report == null) return;
    final s = c.settings;
    double half(double x, double w) => x.abs() + w / 2;
    final halfW = [
      half(s.open.x, s.open.width),
      half(s.closed.x, s.closed.width) + 8 + s.closed.height,
      half(s.closed.x, s.open.width - 28),
    ].reduce(math.max);
    final visible = c.primary != null || c.budsBubble != null;
    // Nothing shows: the notch is invisible, but the camera still takes a tap (the overview) or a double tap.
    final idleTouch = !visible && !c.landscape && (s.shows(ActivityKind.home) || s.doubleTap != NotchGesture.none);
    final width = 2 * halfW + 16;
    if (c.touchRegion) {
      // One size for as long as the notch shows; only where it is takes touches (all of it while open, to close it).
      final tallest = [s.open.y + s.open.height, s.closed.y + _bannerShape(s).height].reduce(math.max) + 14;
      final size = visible || idleTouch || !_shape.settled ? Size(width, tallest) : Size.zero;
      final w = _shape.w.value, h = _shape.h.value;
      final left = width / 2 + _shape.x.value - w / 2;
      final rect = c.expanded
          ? [0.0, 0.0, size.width, size.height]
          : !visible
          // The camera's own place: as big as the closed notch, which isn't drawn.
          ? [width / 2 + s.closed.x - s.closed.width / 2, s.closed.y, s.closed.width, s.closed.height]
          : [left - 4, _shape.y.value - 4, w + 8 + (_bubble.value > 1 ? 6 + _bubble.value : 0), h + 8];
      final rectMoved = _rect == null || [for (var i = 0; i < 4; i++) (rect[i] - _rect![i]).abs()].any((d) => d > 0.5);
      if (size == _window && !rectMoved) return;
      _window = size;
      _rect = rect;
      report(size.width, size.height, visible || idleTouch, rect);
      return;
    }
    final height = math.max(_shape.y.value + _shape.h.value, _target.y + _target.height) + 14;
    var size = visible || !_shape.settled ? Size(width, height) : Size.zero;
    final settled = _shape.settled && _bubble.settled;
    if (!settled && _window != Size.zero) {
      size = Size(math.max(size.width, _window.width), math.max(size.height, _window.height));
    }
    if ((size.width - _window.width).abs() < 1 && (size.height - _window.height).abs() < 1) return;
    _window = size;
    report(size.width, size.height, visible, null);
  }

  /// The corners: all alike while closed; opening, each moves towards its own radius, if it has one.
  BorderRadius _corners(double w, double h) {
    final s = c.settings, open = s.open;
    final most = math.min(w, h) / 2;
    final r = _shape.r.value;
    Radius at(int i) {
      var v = r;
      if (open.corners != null) {
        final t = ((h - s.closed.height) / math.max(_openHeight - s.closed.height, 1)).clamp(0.0, 1.0);
        v += t * (open.corner(i) - open.radius);
      }
      return Radius.circular(v.clamp(0.0, most));
    }

    return BorderRadius.only(topLeft: at(0), topRight: at(1), bottomRight: at(2), bottomLeft: at(3));
  }

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, box) {
      final visible = c.visible;
      final p = visible.isEmpty ? null : visible.first;
      final second = visible.length > 1 ? visible[1] : null;
      final cx = box.maxWidth / 2;
      final w = _shape.w.value, h = _shape.h.value;
      final left = cx + _shape.x.value - w / 2;
      final alpha = _shape.opacity.value.clamp(0.0, 1.0);
      final open = c.expanded;
      return Stack(
        clipBehavior: Clip.none,
        children: [
          // Beside the notch, in the overlay: a tap closes it, a swipe down opens the notifications as the status bar would.
          if (widget.onWindow != null)
            Positioned.fill(
              child: GestureDetector(
                behavior: HitTestBehavior.translucent,
                onTap: c.collapse,
                onVerticalDragEnd: (d) {
                  if ((d.primaryVelocity ?? 0) > 100) c.action?.call('shade', const {});
                },
              ),
            ),
          Positioned(
            left: left,
            top: _shape.y.value,
            width: math.max(w, 1),
            height: math.max(h, 1),
            child: Opacity(
              opacity: alpha,
              child: GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () => open ? null : (p == null ? c.showHome() : c.expand()),
                onDoubleTap: open || c.settings.doubleTap == NotchGesture.none ? null : () => c.gesture(c.settings.doubleTap),
                onLongPress: open ? null : () => c.gesture(c.settings.longPress),
                onVerticalDragEnd: (d) {
                  final v = d.primaryVelocity ?? 0;
                  if (v < -150) c.collapse();
                  if (v > 150) c.expand();
                },
                // A swipe sideways sends it away, as with Android's own pop-ups. Open, it closes the notch, and a
                // notification goes with it (the song bar and the sliders take their own drags first).
                onHorizontalDragUpdate: !open && p?.kind == ActivityKind.volume
                    ? (d) => c.setVolume('keys', (p!.progress ?? 0) + d.delta.dx / math.max(w * 0.6, 1))
                    : null,
                onHorizontalDragEnd: c.typing || (!open && (p == null || p.kind == ActivityKind.volume))
                    ? null
                    : (d) {
                        final v = d.primaryVelocity ?? 0;
                        if (v.abs() <= 200) return;
                        if (!open) {
                          c.swipeAway(p!, clear: v > 0);
                        } else {
                          if (p != null && p.isAlert) c.swipeAway(p, clear: v > 0);
                          c.collapse();
                        }
                      },
                child: ClipRRect(
                  borderRadius: _corners(w, h),
                  child: Stack(
                    fit: StackFit.expand,
                    children: [
                      _Backdrop(c, p, open),
                      // Laid out at the size it is going to, so text doesn't reflow while the shape springs.
                      OverflowBox(
                        alignment: Alignment.topCenter,
                        minWidth: _target.width,
                        maxWidth: _target.width,
                        minHeight: _target.height,
                        maxHeight: _target.height,
                        child: AnimatedSwitcher(
                          duration: const Duration(milliseconds: 220),
                          switchInCurve: const Interval(0.25, 1, curve: Curves.easeOutCubic),
                          switchOutCurve: Curves.easeInCubic,
                          child: p == null
                              ? const SizedBox.shrink()
                              : KeyedSubtree(
                                  key: ValueKey(
                                    '${open
                                        ? 'open'
                                        : p.banner
                                        ? 'banner'
                                        : p.wide
                                        ? 'wide'
                                        : 'closed'}:${p.id}:${second?.id}',
                                  ),
                                  child: open
                                      ? _OwnHeight(
                                          openHeight(c.settings, p, second, c),
                                          NotchPalette.forDesign(_design(c), child: _Open(c, p, second, _target)),
                                        )
                                      : p.banner && c.settings.compactBanners
                                      ? _CompactBanner(c, p, c.settings.closed.height, _target.width)
                                      : p.banner
                                      ? _Banner(c, p, c.settings.closed.height, _bannerInset(_bannerRadius(c.settings)))
                                      : p.wide
                                      ? _Wide(c, p, c.settings.closed.height + 4, _target.width)
                                      : _Closed(c, p, c.settings.closed.height),
                                ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
          if (_bubble.value > 0.5)
            Positioned(
              left: left + w + 6 * (_bubble.value / math.max(c.settings.closed.height, 1)),
              top: _shape.y.value + (c.settings.closed.height - _bubble.value) / 2,
              width: _bubble.value,
              height: _bubble.value,
              child: Opacity(
                opacity: _bubbleAlpha.value.clamp(0.0, 1.0),
                child: c.budsBubble != null
                    ? GestureDetector(onTap: c.openBuds, child: _BudsBubble(c, c.budsBubble!, _bubble.value))
                    : GestureDetector(
                        onTap: second == null
                            ? null
                            : () {
                                c.focus(second);
                                c.expand();
                              },
                        child: Container(
                          decoration: const BoxDecoration(color: Colors.black, shape: BoxShape.circle),
                          alignment: Alignment.center,
                          child: second == null ? null : _Glyph(c, second, size: _bubble.value * 0.55),
                        ),
                      ),
              ),
            ),
        ],
      );
    },
  );
}

/// The open player: black, with a hint of the blurred cover rising from the bottom. It fades out before the camera's
/// band, so the camera and the black around it stay one piece.
class _CoverGlow extends StatelessWidget {
  final CoverColors cover;
  final NotchSettings settings;
  const _CoverGlow({required this.cover, required this.settings});

  @override
  Widget build(BuildContext context) {
    final open = settings.open, closed = settings.closed;
    // Where the camera's band ends inside the open notch, and a little below it.
    final clear = ((closed.y - open.y + closed.height + 12) / open.height).clamp(0.1, 0.8);
    return Stack(
      key: const ValueKey('wash'),
      fit: StackFit.expand,
      children: [
        const ColoredBox(color: Colors.black),
        ShaderMask(
          blendMode: BlendMode.dstIn,
          shaderCallback: (rect) => LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [Colors.transparent, Colors.transparent, Colors.white.withValues(alpha: 0.42)],
            stops: [0, clear, 1],
          ).createShader(rect),
          child: RawImage(image: cover.backdrop, fit: BoxFit.fill, filterQuality: FilterQuality.high),
        ),
      ],
    );
  }
}

/// Black, always while closed (it continues the camera); open, a hint of the music's cover in the Holm look.
class _Backdrop extends StatelessWidget {
  final NotchController c;
  final NotchActivity? p;
  final bool open;
  const _Backdrop(this.c, this.p, this.open);

  @override
  Widget build(BuildContext context) {
    final cover = c.cover;
    final design = _design(c);
    final wash = open && design == null && c.settings.look == NotchLook.holm && p?.kind == ActivityKind.music && cover != null;
    return AnimatedSwitcher(
      duration: const Duration(milliseconds: 300),
      // Filling the notch: the default layout centres children at their own (here: no) size. Black under it all:
      // while the backgrounds cross-fade, neither is fully there, and the camera showed through.
      layoutBuilder: (current, previous) => Stack(
        fit: StackFit.expand,
        children: [
          const ColoredBox(color: Colors.black),
          ...previous,
          ?current,
        ],
      ),
      child: open && design != null
          ? ColoredBox(key: ValueKey('design:${design.id}'), color: design.background)
          : wash
          ? _CoverGlow(cover: cover, settings: c.settings)
          : open && design == null && p?.kind == ActivityKind.buds
          // The earbuds' card: a hint of their own colour from below (white buds white, pink ones pink).
          ? DecoratedBox(
              key: ValueKey('buds:${NotchController.budsModel(p!.title, c.settings.budsColor)}'),
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [Colors.black, Colors.black, Color.alphaBlend(_budsTint(c, p!).withValues(alpha: 0.16), Colors.black)],
                  stops: const [0, 0.4, 1],
                ),
              ),
            )
          : open && c.settings.look == NotchLook.holm && p != null && Brand.of(p!.package) != null
          ? DecoratedBox(
              key: ValueKey('brand:${p!.package}'),
              decoration: BoxDecoration(
                // A hint of the app's colour from below, black around the camera.
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [Colors.black, Colors.black, Color.alphaBlend(Brand.of(p!.package)!.color.withValues(alpha: 0.2), Colors.black)],
                  stops: const [0, 0.45, 1],
                ),
              ),
            )
          : const ColoredBox(key: ValueKey('black'), color: Colors.black),
    );
  }
}

// ───────────── closed ─────────────

/// Closed: something small on the left of the camera and something on the right.
class _Closed extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final double h;
  const _Closed(this.c, this.a, this.h);

  @override
  Widget build(BuildContext context) {
    // The round picture sits concentric in the notch's round end: the same distance to the edge above, below and beside.
    final icon = h * 0.7;
    final inset = (h - icon) / 2;
    return SizedBox(
      height: h,
      child: Padding(
        padding: EdgeInsets.only(left: inset, right: inset + h * 0.12),
        child: Row(
          children: [
            SizedBox(
              height: icon,
              width: icon,
              child: _Glyph(c, a, size: icon),
            ),
            const Spacer(),
            // Never wider than its side of the camera.
            ConstrainedBox(
              constraints: BoxConstraints(maxWidth: math.max(h, c.settings.closed.width / 2 - h * 0.5)),
              child: FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerRight, child: _ClosedTrailing(c, a, h * 0.62)),
            ),
          ],
        ),
      ),
    );
  }
}

class _ClosedTrailing extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final double size;
  const _ClosedTrailing(this.c, this.a, this.size);

  @override
  Widget build(BuildContext context) {
    final accent = c.accentFor(a);
    final style = TextStyle(
      color: accent,
      fontSize: size * 0.62,
      fontWeight: FontWeight.w600,
      fontFeatures: const [FontFeature.tabularFigures()],
    );
    switch (a.kind) {
      case ActivityKind.music:
        return _Equalizer(color: accent, playing: c.media?.playing ?? false, height: size * 0.8);
      case ActivityKind.timer:
        if (!a.on && a.countdownEnd == null && a.countUpStart == null) {
          return Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.pause_rounded, color: accent.withValues(alpha: 0.7), size: size * 0.7),
              Text(_clock(a), style: style.copyWith(color: accent.withValues(alpha: 0.7))),
            ],
          );
        }
        return Text(_clock(a), style: style);
      case ActivityKind.charging:
        return Text(a.level == null ? '' : '${a.level}%', style: style);
      case ActivityKind.call:
        return a.callType == 1
            ? Icon(Icons.phone_in_talk_rounded, color: accent, size: size * 0.8)
            : Text(_clock(a), style: style);
      case ActivityKind.navigation:
        return Text(a.title ?? '', style: style.copyWith(color: Colors.white));
      case ActivityKind.privacy:
        return _Dot(color: accent, size: size * 0.42);
      case ActivityKind.wifi || ActivityKind.vpn || ActivityKind.bluetooth:
        return Icon(a.on ? Icons.check_rounded : Icons.close_rounded, color: accent, size: size * 0.8);
      case ActivityKind.buds:
        return a.level == null
            ? Icon(Icons.headphones_rounded, color: Colors.white, size: size * 0.8)
            : Text('${a.level}%', style: style);
      default:
        if (a.isOffer) return Icon(Icons.download_rounded, color: accent, size: size * 0.8);
        if (a.done) {
          return Icon(
            a.failed ? Icons.error_outline_rounded : Icons.check_circle_rounded,
            color: a.failed ? const Color(0xFFFF453A) : accent,
            size: size * 0.8,
          );
        }
        if (a.hasProgress) return _Ring(progress: a.progress, color: accent, size: size * 0.8);
        if (a.text != null) {
          return ConstrainedBox(
            constraints: BoxConstraints(maxWidth: size * 3),
            child: Marquee(a.text!, style: style.copyWith(fontSize: size * 0.5)),
          );
        }
        return const SizedBox.shrink();
    }
  }
}

/// Short news in the widened pill: the symbol and a name left of the camera, the value right of it, in its colour.
class _Wide extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final double h, width;
  const _Wide(this.c, this.a, this.h, this.width);

  (String, String) get _words => switch (a.kind) {
    ActivityKind.timer => (a.label == null || a.label == 'Clock' ? 'Timer' : a.label!, "Time's up"),
    ActivityKind.charging => (a.title ?? 'Charging', a.level == null ? '' : '${a.level}%'),
    ActivityKind.wifi => ('Wi-Fi', a.on ? 'Connected' : 'Off'),
    ActivityKind.bluetooth => (a.title ?? 'Bluetooth', a.on ? (a.level != null ? '${a.level}%' : 'Connected') : 'Off'),
    ActivityKind.volume => (a.title ?? 'Volume', '${((a.progress ?? 0) * 100).round()}%'),
    ActivityKind.vpn => (a.mullvad ? 'Mullvad' : 'VPN', a.on ? 'On' : 'Off'),
    ActivityKind.download => (a.title ?? 'Download', 'Downloaded'),
    _ => (a.title ?? '', a.text ?? ''),
  };

  @override
  Widget build(BuildContext context) {
    final icon = h * 0.7;
    final (name, value) = _words;
    // Each side of the camera (about as wide as the closed notch is tall) gets half of the rest.
    final side = (width - h * 1.2) / 2 - icon - 18;
    const style = TextStyle(fontSize: 14, fontWeight: FontWeight.w600);
    return SizedBox(
      height: h,
      child: Padding(
        // Left, the round symbol sits concentric in the round end; right, text needs more room in the curve to look even.
        padding: EdgeInsets.only(left: (h - icon) / 2, right: h * 0.5),
        child: Row(
          children: [
            SizedBox(
              width: icon,
              height: icon,
              child: _Glyph(c, a, size: icon),
            ),
            const SizedBox(width: 9),
            ConstrainedBox(
              constraints: BoxConstraints(maxWidth: math.max(0, side)),
              child: Marquee(name, style: style.copyWith(color: Colors.white)),
            ),
            const Spacer(),
            if (a.kind == ActivityKind.volume)
              SizedBox(
                width: math.max(0, side + icon - 6),
                child: _Bar(progress: a.progress ?? 0, color: Colors.white, nothing: c.settings.look == NotchLook.nothing),
              )
            else
            ConstrainedBox(
              constraints: BoxConstraints(maxWidth: math.max(0, side + icon + 9)),
              child: Marquee(value, style: style.copyWith(color: c.accentFor(a))),
            ),
          ],
        ),
      ),
    );
  }
}

/// A compact notification: the closed notch's height. Left of the camera the picture and who wrote, right of it the
/// message; tapping opens it as usual.
class _CompactBanner extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final double h, width;
  const _CompactBanner(this.c, this.a, this.h, this.width);

  @override
  Widget build(BuildContext context) {
    final icon = h * 0.7;
    // Each side of the camera (about as wide as the notch is tall) gets half of the rest.
    final side = (width - h * 1.2) / 2 - icon - 12;
    final text = a.text != null && a.text != a.title ? a.text! : (a.label ?? '');
    return SizedBox(
      height: h,
      child: Padding(
        padding: EdgeInsets.only(left: (h - icon) / 2, right: h * 0.5),
        child: Row(
          children: [
            SizedBox(width: icon, height: icon, child: _Glyph(c, a, size: icon)),
            const SizedBox(width: 9),
            ConstrainedBox(
              constraints: BoxConstraints(maxWidth: math.max(0, side)),
              child: Marquee(a.title ?? a.label ?? '', style: const TextStyle(color: Colors.white, fontSize: 13.5, fontWeight: FontWeight.w600)),
            ),
            const Spacer(),
            ConstrainedBox(
              constraints: BoxConstraints(maxWidth: math.max(0, side + icon + 9)),
              child: Marquee(text, style: const TextStyle(color: _dim, fontSize: 13)),
            ),
          ],
        ),
      ),
    );
  }
}

/// A notification while closed: the picture concentric in the rounded end, the same margin all round, the name with
/// the source and time at the right end of its line, the message under it.
class _Banner extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final double band;
  final double inset;
  const _Banner(this.c, this.a, this.band, this.inset);

  @override
  Widget build(BuildContext context) {
    final text = a.text != null && a.text != a.title ? a.text : null;
    final source = [a.label ?? a.kind.label, _ago(a.created)].join(' · ');
    return Padding(
      // The text keeps a little more room at its end than the picture at its side: the round end curves in there.
      padding: EdgeInsets.fromLTRB(inset, inset, inset + 8, inset),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            height: _bannerIcon,
            child: Row(
              children: [
                SizedBox(
                  width: _bannerIcon,
                  height: _bannerIcon,
                  child: _Glyph(c, a, size: _bannerIcon),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.baseline,
                        textBaseline: TextBaseline.alphabetic,
                        children: [
                          Expanded(
                            child: Marquee(a.title ?? '', style: const TextStyle(color: Colors.white, fontSize: 14, fontWeight: FontWeight.w600, height: 1.2)),
                          ),
                          const SizedBox(width: 12),
                          Text(source, maxLines: 1, style: const TextStyle(color: _faint, fontSize: 12)),
                        ],
                      ),
                      if (text != null)
                        Marquee(text, style: const TextStyle(color: _dim, fontSize: 13, height: 1.25)),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

// ───────────── open ─────────────

class _Open extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final NotchActivity? second;
  final NotchGeometry g;
  const _Open(this.c, this.a, this.second, this.g);

  bool get nothing => c.settings.look == NotchLook.nothing;

  @override
  Widget build(BuildContext context) {
    final s = c.settings;
    // The top band holds small things left and right of the camera; the content starts below it.
    final band = (s.closed.y - g.y + s.closed.height + 4).clamp(28.0, 80.0);
    return Padding(
      // A little more at the right: text ending there sits in the corner's curve and looks closer to the edge than it is.
      padding: const EdgeInsets.fromLTRB(_inset, 0, _inset + 6, _inset),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            height: band,
            child: Row(
              children: [
                // The name fills the line, so the time sits at its right end (a Flexible beside a Spacer left it mid-way).
                Expanded(
                  child: nothing
                      ? Marquee(
                          (a.label ?? a.kind.label).toUpperCase(),
                          style: TextStyle(color: NotchPalette.of(context).dim, fontSize: 11, fontFamily: 'monospace', letterSpacing: 1.5),
                        )
                      : Marquee(a.label ?? a.kind.label, style: TextStyle(color: NotchPalette.of(context).dim, fontSize: 12.5)),
                ),
                const SizedBox(width: 12),
                if (nothing) const _RedDot() else _OpenCorner(c, a),
              ],
            ),
          ),
          Expanded(child: _OpenBody(c, a, nothing)),
          if (second != null) ...[
            Divider(height: 14, color: NotchPalette.of(context).ink.withValues(alpha: 0.12)),
            GestureDetector(
              onTap: () => c.focus(second!),
              child: Row(
                children: [
                  SizedBox(width: 22, height: 22, child: _Glyph(c, second!, size: 22)),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Marquee(_summary(second!), style: TextStyle(color: NotchPalette.of(context).dim, fontSize: 13, fontFamily: nothing ? 'monospace' : null)),
                  ),
                  Icon(Icons.unfold_more_rounded, size: 16, color: NotchPalette.of(context).faint),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _OpenCorner extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  const _OpenCorner(this.c, this.a);

  @override
  Widget build(BuildContext context) => switch (a.kind) {
    ActivityKind.music => _Equalizer(color: c.accentFor(a), playing: c.media?.playing ?? false, height: 14),
    ActivityKind.charging when a.level != null => Text('${a.level}%', style: TextStyle(color: NotchPalette.of(context).dim, fontSize: 12.5)),
    _ when c.canMute(a) => Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(_ago(a.created), style: TextStyle(color: NotchPalette.of(context).faint, fontSize: 12)),
        const SizedBox(width: 6),
        // Quiet for 5 minutes.
        Semantics(
          button: true,
          label: 'Mute for 5 minutes',
          child: GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTap: () => c.snooze(a),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(6, 6, 4, 6),
              child: Icon(Icons.snooze_rounded, size: 16, color: NotchPalette.of(context).faint),
            ),
          ),
        ),
        // Don't show this app again (it can be switched back on in Holm's notch settings).
        GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: () => c.mute(a),
          child: Padding(
            padding: EdgeInsets.fromLTRB(6, 6, 0, 6),
            child: Icon(Icons.notifications_off_outlined, size: 16, color: NotchPalette.of(context).faint),
          ),
        ),
      ],
    ),
    _ => Text(_ago(a.created), style: TextStyle(color: NotchPalette.of(context).faint, fontSize: 12)),
  };
}

class _OpenBody extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final bool nothing;
  const _OpenBody(this.c, this.a, this.nothing);

  void _open() {
    if (a.notificationKey != null) {
      c.action?.call('open', {'key': a.notificationKey, 'page': a.page});
    } else if (a.page != null) {
      c.action?.call('openApp', {'page': a.page});
    }
    c.dismiss(a);
    c.collapse();
  }

  @override
  Widget build(BuildContext context) {
    final accent = nothing ? _nothingRed : c.accentFor(a);
    final title = a.title ?? a.kind.label;
    Widget headline(String text, {double size = 19}) => nothing
        ? FittedBox(
            fit: BoxFit.scaleDown,
            alignment: Alignment.centerLeft,
            child: DotText(text, dot: size / 7.5, maxChars: 18),
          )
        : Marquee(text, style: TextStyle(color: NotchPalette.of(context).ink, fontSize: size, fontWeight: FontWeight.w600));
    Widget body(String? text, {int lines = 2}) => text == null
        ? const SizedBox.shrink()
        : Text(
            text,
            maxLines: lines,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(color: NotchPalette.of(context).dim, fontSize: nothing ? 13 : 14.5, height: 1.3, fontFamily: nothing ? 'monospace' : null),
          );

    switch (a.kind) {
      case ActivityKind.music:
        return _Player(c, nothing);
      case ActivityKind.buds:
        final de = ui.PlatformDispatcher.instance.locale.languageCode == 'de';
        // Each bud and the case, when Google Play services told them; else the pair's one level.
        final parts = [
          if (c.budsLeft != null) ('L', c.budsLeft!),
          if (c.budsCase != null) (de ? 'Etui' : 'Case', c.budsCase!),
          if (c.budsRight != null) ('R', c.budsRight!),
        ];
        return Row(
          children: [
            SizedBox(width: 112, height: 112, child: _BudsOut(c, a)),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  headline(title, size: 17),
                  const SizedBox(height: 10),
                  if (parts.isNotEmpty)
                    Row(
                      children: [
                        for (final (name, level) in parts) ...[
                          _BudLevel(name, level, nothing: nothing),
                          const SizedBox(width: 12),
                        ],
                      ],
                    )
                  else ...[
                    body(a.level == null ? (de ? 'Verbunden' : 'Connected') : (de ? 'Akku ${a.level}%' : 'Battery ${a.level}%'), lines: 1),
                    if (a.level != null) ...[
                      const SizedBox(height: 10),
                      _Bar(progress: a.level! / 100, color: accent, nothing: nothing),
                    ],
                  ],
                ],
              ),
            ),
          ],
        );
      case ActivityKind.home:
        return _Home(c, nothing);
      case ActivityKind.call:
        final ringing = a.callType == 1;
        final radius = _buttonRadius(c.settings);
        return Column(
          children: [
            Row(
              children: [
                SizedBox(width: 44, height: 44, child: _Glyph(c, a, size: 44)),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      headline(title, size: 18),
                      const SizedBox(height: 2),
                      body(ringing ? 'Incoming call' : _clock(a), lines: 1),
                    ],
                  ),
                ),
              ],
            ),
            const Spacer(),
            Row(
              children: ringing
                  ? [
                      Expanded(child: _Button('Decline', color: const Color(0xFFFF453A), nothing: nothing, radius: radius, onTap: () => c.callAction(a, 'decline'))),
                      const SizedBox(width: 10),
                      Expanded(child: _Button('Answer', color: const Color(0xFF30D158), nothing: nothing, radius: radius, onTap: () => c.callAction(a, 'answer'))),
                    ]
                  : [
                      Expanded(child: _Button(c.callMuted ? 'Unmute' : 'Mute', filled: false, nothing: nothing, radius: radius, onTap: c.muteCall)),
                      const SizedBox(width: 10),
                      Expanded(child: _Button('Hang up', color: const Color(0xFFFF453A), nothing: nothing, radius: radius, onTap: () => c.callAction(a, 'hangUp'))),
                    ],
            ),
          ],
        );
      case ActivityKind.navigation:
        return GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: _open,
          child: Row(
            children: [
              SizedBox(width: 52, height: 52, child: _Glyph(c, a, size: 52)),
              const SizedBox(width: 16),
              Expanded(
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [headline(title, size: 24), const SizedBox(height: 2), body(a.text, lines: 1)],
                ),
              ),
            ],
          ),
        );
      case ActivityKind.screenshot:
        return Row(
          children: [
            ClipRRect(
              borderRadius: BorderRadius.circular(nothing ? 4 : 12),
              child: SizedBox(
                width: 62,
                height: 92,
                child: a.picture != null
                    ? Image.memory(a.picture!, fit: BoxFit.cover)
                    : ColoredBox(color: NotchPalette.of(context).ink.withValues(alpha: 0.1)),
              ),
            ),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  headline(title, size: 17),
                  const SizedBox(height: 2),
                  body(a.text, lines: 1),
                  const SizedBox(height: 12),
                  _Button('Send', nothing: nothing, radius: _buttonRadius(c.settings), onTap: () => c.beamScreenshot(a)),
                ],
              ),
            ),
          ],
        );
      case ActivityKind.clipboard:
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            headline(title, size: 17),
            const SizedBox(height: 3),
            body(a.text, lines: 3),
            const Spacer(),
            _Button('Copy again', filled: false, nothing: nothing, radius: _buttonRadius(c.settings), onTap: () {
              Clipboard.setData(ClipboardData(text: a.text ?? ''));
              c.dismiss(a);
            }),
          ],
        );
      case ActivityKind.privacy:
        return Row(
          children: [
            Icon(a.on ? Icons.videocam_rounded : Icons.mic_rounded, color: accent, size: 32),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [headline('$title in use'), const SizedBox(height: 2), body(a.text ?? 'By an app', lines: 1)],
              ),
            ),
          ],
        );
      case ActivityKind.volume:
        // Every volume, one under the other, each set with a finger.
        const rows = [
          ('media', 'Media', Icons.music_note_rounded),
          ('ring', 'Ringtone', Icons.ring_volume_rounded),
          ('notification', 'Notifications', Icons.notifications_rounded),
          ('alarm', 'Alarm', Icons.alarm_rounded),
          ('call', 'Calls', Icons.call_rounded),
        ];
        return Column(
          children: [
            for (final (stream, label, icon) in rows)
              SizedBox(
                height: 32,
                child: Row(
                  children: [
                    Icon(icon, size: 17, color: NotchPalette.of(context).dim),
                    const SizedBox(width: 10),
                    SizedBox(
                      width: 112,
                      child: Marquee(label, style: TextStyle(color: NotchPalette.of(context).ink, fontSize: nothing ? 12 : 13.5, fontFamily: nothing ? 'monospace' : null)),
                    ),
                    Expanded(
                      child: _VolumeSlider(
                        value: c.volumes[stream] ?? (stream == 'media' ? a.progress ?? 0 : 0),
                        color: nothing ? _nothingRed : NotchPalette.of(context).accent,
                        onChanged: (v) => c.setVolume(stream, v),
                      ),
                    ),
                  ],
                ),
              ),
          ],
        );
      case ActivityKind.timer:
        return Column(
          children: [
            Expanded(
              child: GestureDetector(
                onTap: _open,
                child: Row(
                  children: [
                    Icon(a.recording ? Icons.fiber_manual_record_rounded : Icons.timer_outlined, color: accent, size: 30),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          if (a.done)
                            headline("Time's up", size: 26)
                          else if (nothing)
                            DotText(_clock(a), dot: 6)
                          else
                            Text(
                              _clock(a),
                              style: TextStyle(
                                color: NotchPalette.of(context).ink,
                                fontSize: 44,
                                fontWeight: FontWeight.w300,
                                fontFeatures: [FontFeature.tabularFigures()],
                              ),
                            ),
                          // A line under the time only when it says something: a timer's own name, or that it is paused.
                          if (!a.done && (a.text != null && a.text != _clock(a) || !const {'Timer', 'Stopwatch'}.contains(title)))
                            body(a.text != null && a.text != _clock(a) ? '$title · paused' : title, lines: 1),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
            if (a.actions.isNotEmpty) _Actions(c, a, nothing),
          ],
        );
      case ActivityKind.charging:
        return Row(
          children: [
            Icon(a.on ? Icons.bolt_rounded : Icons.power_off_rounded, color: accent, size: 34),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  headline(a.level == null ? title : '$title · ${a.level}%'),
                  const SizedBox(height: 12),
                  _Bar(progress: (a.level ?? 0) / 100, color: accent, nothing: nothing),
                ],
              ),
            ),
          ],
        );
      case ActivityKind.vpn:
        return Row(
          children: [
            if (a.mullvad)
              Mole(up: a.on, size: 64)
            else
              Icon(a.on ? Icons.vpn_lock_rounded : Icons.no_encryption_outlined, color: accent, size: 34),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [headline(title), const SizedBox(height: 4), body(a.text, lines: 1)],
              ),
            ),
          ],
        );
      default:
        if (a.isOffer) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  _Initials(a.title ?? '?', 40),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        headline(a.label == 'Text' ? 'Text from ${a.title}' : '${a.title} sends you', size: 17),
                        body(a.text, lines: 1),
                      ],
                    ),
                  ),
                ],
              ),
              const Spacer(),
              Row(
                children: [
                  Expanded(
                    child: _Button(
                      'Decline',
                      filled: false,
                      nothing: nothing,
                      radius: _buttonRadius(c.settings),
                      onTap: () => c.answerOffer(a, false),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: _Button(
                      a.label == 'Text' ? 'Copy' : 'Accept',
                      nothing: nothing,
                      radius: _buttonRadius(c.settings),
                      onTap: () => c.answerOffer(a, true),
                    ),
                  ),
                ],
              ),
            ],
          );
        }
        return Column(
          children: [
            Expanded(
              child: GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: _open,
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    SizedBox(width: 44, height: 44, child: _Glyph(c, a, size: 44)),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          headline(title, size: 17),
                          const SizedBox(height: 3),
                          body(a.text, lines: a.hasProgress || (c.typing && identical(a, c.primary)) ? 1 : 3),
                          if (a.hasProgress && !a.done) ...[
                            const SizedBox(height: 12),
                            Row(
                              children: [
                                Expanded(
                                  child: _Bar(progress: a.progress, color: accent, nothing: nothing),
                                ),
                                if (a.progress != null) ...[
                                  const SizedBox(width: 10),
                                  Text('${(a.progress! * 100).round()}%', style: TextStyle(color: NotchPalette.of(context).dim, fontSize: 12.5)),
                                ],
                              ],
                            ),
                          ],
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
            if (c.typing && a.replyIndex != null)
              _ReplyField(c, a, nothing)
            else if (a.actions.isNotEmpty || a.replyIndex != null)
              _Actions(c, a, nothing),
          ],
        );
    }
  }
}

/// A volume to set with a finger: tap or drag anywhere along it.
class _VolumeSlider extends StatelessWidget {
  final double value;
  final Color color;
  final ValueChanged<double> onChanged;
  const _VolumeSlider({required this.value, required this.color, required this.onChanged});

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, box) {
      void at(Offset p) => onChanged((p.dx / math.max(box.maxWidth, 1)).clamp(0.0, 1.0));
      return GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTapDown: (d) => at(d.localPosition),
        onHorizontalDragStart: (d) => at(d.localPosition),
        onHorizontalDragUpdate: (d) => at(d.localPosition),
        child: SizedBox(
          height: 28,
          child: Center(
            child: Stack(
              alignment: Alignment.centerLeft,
              children: [
                Container(height: 6, decoration: BoxDecoration(color: NotchPalette.of(context).card, borderRadius: BorderRadius.circular(3))),
                FractionallySizedBox(
                  widthFactor: value.clamp(0.0, 1.0),
                  child: Container(height: 6, decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(3))),
                ),
              ],
            ),
          ),
        ),
      );
    },
  );
}

/// Answering a message in the notch: a line to type in and a send button.
class _ReplyField extends StatefulWidget {
  final NotchController c;
  final NotchActivity a;
  final bool nothing;
  const _ReplyField(this.c, this.a, this.nothing);

  @override
  State<_ReplyField> createState() => _ReplyFieldState();
}

class _ReplyFieldState extends State<_ReplyField> {
  final _text = TextEditingController();
  bool _sending = false;

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    if (_sending || _text.text.trim().isEmpty) return;
    setState(() => _sending = true);
    final ok = await widget.c.reply(widget.a, _text.text);
    if (!ok && mounted) setState(() => _sending = false);
  }

  @override
  Widget build(BuildContext context) {
    final radius = math.min(_buttonRadius(widget.c.settings), 21.0);
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Container(
        height: 42,
        padding: const EdgeInsets.only(left: 14, right: 4),
        decoration: BoxDecoration(
          color: NotchPalette.of(context).ink.withValues(alpha: widget.nothing ? 0 : 0.12),
          border: widget.nothing ? Border.all(color: NotchPalette.of(context).ink.withValues(alpha: 0.4)) : null,
          borderRadius: BorderRadius.circular(radius),
        ),
        child: Row(
          children: [
            Expanded(
              child: TextField(
                controller: _text,
                autofocus: true,
                textInputAction: TextInputAction.send,
                onSubmitted: (_) => _send(),
                cursorColor: NotchPalette.of(context).ink,
                style: TextStyle(color: NotchPalette.of(context).ink, fontSize: 15, fontFamily: widget.nothing ? 'monospace' : null),
                decoration: InputDecoration(
                  hintText: 'Reply',
                  hintStyle: TextStyle(color: NotchPalette.of(context).faint),
                  border: InputBorder.none,
                  isCollapsed: true,
                ),
              ),
            ),
            GestureDetector(
              behavior: HitTestBehavior.opaque,
              onTap: _send,
              child: Padding(
                padding: const EdgeInsets.all(8),
                child: _sending
                    ? SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2, color: NotchPalette.of(context).ink))
                    : Icon(Icons.arrow_upward_rounded, color: widget.nothing ? NotchPalette.of(context).ink : NotchPalette.of(context).accent),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The overview, with a tap on the camera while nothing shows: the time, the batteries, the next alarm and event.
class _Home extends StatelessWidget {
  final NotchController c;
  final bool nothing;
  const _Home(this.c, this.nothing);

  static String _hm(DateTime t) => '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now();
    final locale = ui.PlatformDispatcher.instance.locale.languageCode;
    final de = locale == 'de';
    final days = de ? const ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'] : const ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    final date = '${days[now.weekday - 1]} ${now.day}.${now.month}.';
    final chips = <(IconData, String)>[
      if (c.batteryLevel != null) (c.plugged ? Icons.bolt_rounded : Icons.battery_5_bar_rounded, '${c.batteryLevel}%'),
      for (final e in c.headphones.entries.take(1)) (Icons.headphones_rounded, '${e.value}%'),
      if (c.nextAlarm != null) (Icons.alarm_rounded, _hm(c.nextAlarm!)),
      if (c.nextEvent != null) (Icons.event_rounded, '${_hm(c.nextEvent!.begin)} ${c.nextEvent!.title}'),
      if (c.devices.isNotEmpty) (Icons.laptop_rounded, c.devices.first),
    ];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            nothing
                ? DotText(_hm(now), dot: 5)
                : Text(
                    _hm(now),
                    style: TextStyle(
                      color: NotchPalette.of(context).ink,
                      fontSize: 44,
                      height: 1,
                      fontWeight: FontWeight.w300,
                      fontFeatures: [FontFeature.tabularFigures()],
                    ),
                  ),
            const SizedBox(width: 12),
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Text(date, style: TextStyle(color: NotchPalette.of(context).dim, fontSize: 15, fontFamily: nothing ? 'monospace' : null)),
            ),
          ],
        ),
        const Spacer(),
        Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Expanded(child: Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final (icon, text) in chips)
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
                decoration: BoxDecoration(
                  color: NotchPalette.of(context).ink.withValues(alpha: nothing ? 0 : 0.1),
                  border: nothing ? Border.all(color: NotchPalette.of(context).ink.withValues(alpha: 0.3)) : null,
                  borderRadius: BorderRadius.circular(14),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(icon, size: 15, color: NotchPalette.of(context).dim),
                    const SizedBox(width: 6),
                    ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 150),
                      child: Marquee(text, style: TextStyle(color: NotchPalette.of(context).ink, fontSize: 13, fontFamily: nothing ? 'monospace' : null)),
                    ),
                  ],
                ),
              ),
          ],
        )),
            const SizedBox(width: 8),
            // The notch's settings, in the app.
            Semantics(
              button: true,
              label: de ? 'Einstellungen' : 'Settings',
              child: GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () {
                  c.collapse();
                  c.action?.call('openApp', {'page': 'notch'});
                },
                child: Container(
                  width: 32,
                  height: 32,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: NotchPalette.of(context).ink.withValues(alpha: nothing ? 0 : 0.1),
                    border: nothing ? Border.all(color: NotchPalette.of(context).ink.withValues(alpha: 0.3)) : null,
                  ),
                  child: Icon(Icons.settings_rounded, size: 17, color: NotchPalette.of(context).dim),
                ),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

/// The player, as in Holm's open notch: cover, title and artist, the song's progress and the three buttons.
class _Player extends StatelessWidget {
  final NotchController c;
  final bool nothing;
  const _Player(this.c, this.nothing);

  @override
  Widget build(BuildContext context) {
    final m = c.media;
    final accent = nothing ? _nothingRed : (c.cover?.accent ?? NotchPalette.of(context).ink);
    final dur = m?.duration ?? 0;
    void act(String action, [int? position]) => c.action?.call('media', {'action': action, 'position': position});
    final controls = Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        _MediaButton(Icons.skip_previous_rounded, 28, nothing, () => act('previous')),
        const SizedBox(width: 24),
        _MediaButton((m?.playing ?? false) ? Icons.pause_rounded : Icons.play_arrow_rounded, 38, nothing, () => act('toggle')),
        const SizedBox(width: 24),
        _MediaButton(Icons.skip_next_rounded, 28, nothing, () => act('next')),
      ],
    );
    // However tall the person made the open notch: the song's line goes first, then the buttons move up beside the cover.
    return LayoutBuilder(
      builder: (context, box) {
        final roomy = box.maxHeight >= 132, compact = box.maxHeight < 104;
        return Column(
          children: [
            Row(
              children: [
                ClipRRect(
                  borderRadius: BorderRadius.circular(nothing ? 4 : 12),
                  child: SizedBox(
                    width: 56,
                    height: 56,
                    child: c.art != null
                        ? Image.memory(c.art!, fit: BoxFit.cover, gaplessPlayback: true)
                        : ColoredBox(
                            color: NotchPalette.of(context).ink.withValues(alpha: 0.1),
                            child: Icon(Icons.music_note_rounded, color: NotchPalette.of(context).dim),
                          ),
                  ),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      nothing
                          ? FittedBox(
                              fit: BoxFit.scaleDown,
                              alignment: Alignment.centerLeft,
                              child: DotText(m?.title ?? 'Nothing playing', dot: 2.6, maxChars: 22),
                            )
                          : Marquee(m?.title ?? 'Nothing playing', style: TextStyle(color: NotchPalette.of(context).ink, fontSize: 17, fontWeight: FontWeight.w600)),
                      const SizedBox(height: 3),
                      Marquee(m?.artist ?? '', style: TextStyle(color: NotchPalette.of(context).dim, fontSize: 14, fontFamily: nothing ? 'monospace' : null)),
                    ],
                  ),
                ),
                if (compact)
                  _MediaButton((m?.playing ?? false) ? Icons.pause_rounded : Icons.play_arrow_rounded, 32, nothing, () => act('toggle')),
              ],
            ),
            const Spacer(),
            if (dur > 0 && roomy)
              _SongProgress(c, accent: accent, nothing: nothing, onSeek: (ms) => act('seek', ms)),
            if (!compact) controls,
          ],
        );
      },
    );
  }
}

/// The song's progress with the time gone and the time left. It moves every frame while the music plays (the player
/// reports its position only now and then; the rest is counted from it), and a tap seeks where the player allows.
class _SongProgress extends StatefulWidget {
  final NotchController c;
  final Color accent;
  final bool nothing;
  final void Function(int ms)? onSeek;
  const _SongProgress(this.c, {required this.accent, required this.nothing, this.onSeek});

  @override
  State<_SongProgress> createState() => _SongProgressState();
}

class _SongProgressState extends State<_SongProgress> with SingleTickerProviderStateMixin {
  late final Ticker _ticker = createTicker((_) => setState(() {}));
  /// Where a finger holds the song (0..1) while dragging; null otherwise.
  double? _held;
  /// Where it was let go, until the player reports from there (else the bar jumps back for a moment).
  int? _sought;
  DateTime _soughtAt = DateTime(0);

  @override
  void initState() {
    super.initState();
    _follow();
  }

  @override
  void didUpdateWidget(_SongProgress old) {
    super.didUpdateWidget(old);
    _follow();
  }

  void _follow() {
    final playing = widget.c.media?.playing ?? false;
    if (playing && !_ticker.isActive) _ticker.start();
    if (!playing && _ticker.isActive) _ticker.stop();
  }

  @override
  void dispose() {
    _ticker.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final m = widget.c.media;
    final dur = m?.duration ?? 0;
    var pos = m?.positionAt(widget.c.mediaReceived) ?? 0;
    if (_held != null) {
      pos = (_held! * dur).round();
    } else if (_sought != null) {
      if (DateTime.now().difference(_soughtAt) < const Duration(seconds: 2) && (pos - _sought!).abs() > 2000) {
        pos = _sought!; // the player hasn't caught up yet
      } else {
        _sought = null;
      }
    }
    final time = TextStyle(color: NotchPalette.of(context).faint, fontSize: 11, fontFeatures: [FontFeature.tabularFigures()]);
    return LayoutBuilder(
      builder: (context, box) {
        double at(Offset p) => (p.dx / math.max(box.maxWidth, 1)).clamp(0.0, 1.0);
        void seek(double f) {
          final ms = (f * dur).round();
          _sought = ms;
          _soughtAt = DateTime.now();
          widget.onSeek!(ms);
        }
        final can = widget.onSeek != null && dur > 0;
        return GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTapUp: !can ? null : (d) => setState(() => seek(at(d.localPosition))),
        onHorizontalDragStart: !can ? null : (d) => setState(() => _held = at(d.localPosition)),
        onHorizontalDragUpdate: !can ? null : (d) => setState(() => _held = at(d.localPosition)),
        onHorizontalDragEnd: !can
            ? null
            : (_) => setState(() {
                  if (_held != null) seek(_held!);
                  _held = null;
                }),
        onHorizontalDragCancel: !can ? null : () => setState(() => _held = null),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 6),
          child: Row(
            children: [
              Text(_mmss(pos), style: time),
              const SizedBox(width: 8),
              Expanded(
                child: _Bar(progress: dur > 0 ? pos / dur : 0, color: widget.accent, nothing: widget.nothing),
              ),
              const SizedBox(width: 8),
              Text('-${_mmss(dur - pos)}', style: time),
            ],
          ),
        ),
      );
      },
    );
  }
}

/// Lays a content out at its own height and clips it: while one cross-fades into another of a different height, the
/// one leaving keeps its layout instead of being squeezed into the new size.
class _OwnHeight extends StatelessWidget {
  final double height;
  final Widget child;
  const _OwnHeight(this.height, this.child);

  @override
  Widget build(BuildContext context) => ClipRect(
    child: OverflowBox(alignment: Alignment.topCenter, minHeight: height, maxHeight: height, child: child),
  );
}

/// The notification's own buttons ("Stop", "Add 1 min", "Mark as read"), the first one filled.
class _Actions extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final bool nothing;
  const _Actions(this.c, this.a, this.nothing);

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 12),
    child: Row(
      children: [
        if (a.replyIndex != null) ...[
          Expanded(
            child: _Button('Reply', nothing: nothing, radius: _buttonRadius(c.settings), onTap: () => c.startTyping(a)),
          ),
          if (a.actions.isNotEmpty) const SizedBox(width: 8),
        ],
        for (var i = 0; i < a.actions.length && i < (a.replyIndex != null ? 2 : 3); i++) ...[
          if (i > 0) const SizedBox(width: 8),
          Expanded(
            child: _Button(
              a.actions[i].$2,
              filled: i == 0 && a.replyIndex == null,
              nothing: nothing,
              radius: _buttonRadius(c.settings),
              onTap: () => c.pressAction(a, a.actions[i].$1),
            ),
          ),
        ],
      ],
    ),
  );
}

class _MediaButton extends StatelessWidget {
  final IconData icon;
  final double size;
  final bool nothing;
  final VoidCallback onTap;
  const _MediaButton(this.icon, this.size, this.nothing, this.onTap);

  @override
  Widget build(BuildContext context) => GestureDetector(
    behavior: HitTestBehavior.opaque,
    onTap: onTap,
    child: Container(
      width: size + 14,
      height: size + 14,
      decoration: nothing
          ? BoxDecoration(
              shape: BoxShape.circle,
              border: Border.all(color: NotchPalette.of(context).ink.withValues(alpha: 0.35)),
            )
          : null,
      alignment: Alignment.center,
      child: Icon(icon, color: NotchPalette.of(context).ink, size: nothing ? size * 0.7 : size),
    ),
  );
}

class _Button extends StatelessWidget {
  final String text;
  final bool filled, nothing;
  final double radius;
  final VoidCallback onTap;
  final Color? color;
  const _Button(this.text, {this.filled = true, required this.nothing, this.radius = 20, required this.onTap, this.color});

  @override
  Widget build(BuildContext context) => GestureDetector(
    onTap: onTap,
    child: Container(
      height: 42,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: filled
            ? (color ?? (nothing ? NotchPalette.of(context).ink : NotchPalette.of(context).accent))
            : NotchPalette.of(context).ink.withValues(alpha: nothing ? 0 : 0.12),
        border: nothing && !filled ? Border.all(color: NotchPalette.of(context).ink.withValues(alpha: 0.4)) : null,
        borderRadius: BorderRadius.circular(math.min(radius, 21)),
      ),
      child: Text(
        nothing ? text.toUpperCase() : text,
        style: TextStyle(
          color: nothing && filled && color == null
              ? Colors.black
              : filled && color == null
              ? NotchPalette.of(context).onAccent
              : NotchPalette.of(context).ink,
          fontWeight: FontWeight.w600,
          fontFamily: nothing ? 'monospace' : null,
          letterSpacing: nothing ? 1.2 : 0,
        ),
      ),
    ),
  );
}

// ───────────── small parts ─────────────

/// The activity's picture: the cover, the app's icon (with its brand ring), or a symbol.
class _Glyph extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final double size;
  const _Glyph(this.c, this.a, {required this.size});

  @override
  Widget build(BuildContext context) {
    final accent = c.accentFor(a);
    Widget symbol(IconData icon) => Icon(icon, color: accent, size: size * 0.85);
    switch (a.kind) {
      case ActivityKind.music:
        // Round, like the ends of the notch it sits in.
        return ClipOval(
          child: c.art != null
              ? Image.memory(c.art!, width: size, height: size, fit: BoxFit.cover, gaplessPlayback: true)
              : symbol(Icons.music_note_rounded),
        );
      case ActivityKind.timer:
        return symbol(a.recording ? Icons.fiber_manual_record_rounded : a.done ? Icons.alarm_on_rounded : Icons.timer_outlined);
      case ActivityKind.charging:
        return symbol(a.on ? Icons.bolt_rounded : Icons.power_off_rounded);
      case ActivityKind.wifi:
        return symbol(a.on ? Icons.wifi_rounded : Icons.wifi_off_rounded);
      case ActivityKind.bluetooth:
        return symbol(a.on ? Icons.bluetooth_connected_rounded : Icons.bluetooth_disabled_rounded);
      case ActivityKind.vpn:
        return a.mullvad ? Mole(up: a.on, size: size) : symbol(a.on ? Icons.vpn_lock_rounded : Icons.no_encryption_outlined);
      case ActivityKind.holm:
        if (a.isOffer) return _Initials(a.title ?? '?', size);
        return _HolmMark(size);
      case ActivityKind.chat:
        return _Initials(a.title ?? '?', size);
      case ActivityKind.call:
        return a.picture != null
            ? ClipOval(child: Image.memory(a.picture!, width: size, height: size, fit: BoxFit.cover))
            : _Initials(a.title ?? '?', size);
      case ActivityKind.navigation:
        // Maps draws its arrow dark on light: white on the black notch.
        return a.picture != null
            ? ColorFiltered(
                colorFilter: const ColorFilter.mode(Colors.white, BlendMode.srcIn),
                child: Image.memory(a.picture!, width: size, height: size),
              )
            : symbol(Icons.turn_right_rounded);
      case ActivityKind.privacy:
        return symbol(a.on ? Icons.videocam_rounded : Icons.mic_rounded);
      case ActivityKind.volume:
        return Icon(a.on ? Icons.volume_up_rounded : Icons.volume_off_rounded, color: Colors.white, size: size * 0.85);
      case ActivityKind.clipboard:
        return symbol(Icons.content_paste_rounded);
      case ActivityKind.screenshot:
        return a.picture != null
            ? ClipRRect(borderRadius: BorderRadius.circular(size * 0.2), child: Image.memory(a.picture!, width: size, height: size, fit: BoxFit.cover))
            : symbol(Icons.screenshot_rounded);
      case ActivityKind.home:
        return symbol(Icons.access_time_rounded);
      case ActivityKind.buds:
        return _BudsStill(c, a, size);
      default:
        final icon = a.package == null ? null : c.icons[a.package];
        if (icon == null) {
          return symbol(switch (a.kind) {
            ActivityKind.download => Icons.download_rounded,
            ActivityKind.quickShare => Icons.share_rounded,
            ActivityKind.reminder => Icons.notifications_active_outlined,
            _ => Icons.notifications_none_rounded,
          });
        }
        final brand = Brand.of(a.package);
        return Container(
          padding: EdgeInsets.all(brand != null ? size * 0.06 : 0),
          decoration: brand != null
              ? BoxDecoration(
                  shape: BoxShape.circle,
                  gradient: SweepGradient(colors: [brand.color, brand.second ?? brand.color, brand.color]),
                )
              : null,
          child: ClipOval(
            child: Image.memory(icon, width: size, height: size, fit: BoxFit.cover, gaplessPlayback: true),
          ),
        );
    }
  }
}

/// The buds alone, out of their case (a still rendered from their 3D model), or a headphones symbol for earbuds Holm
/// has no model of.
class _BudsStill extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final double size;
  const _BudsStill(this.c, this.a, this.size);

  @override
  Widget build(BuildContext context) {
    final model = NotchController.budsModel(a.title, c.settings.budsColor);
    final symbol = Icon(Icons.headphones_rounded, color: Colors.white, size: size * 0.8);
    if (model == null) return symbol;
    return Image.asset(
      'assets/buds/${model}_buds.webp',
      width: size,
      height: size,
      fit: BoxFit.contain,
      filterQuality: FilterQuality.medium,
      errorBuilder: (_, _, _) => symbol,
    );
  }
}

/// The colour of the earbuds themselves, for their card's glow: white, or the Pixel Buds' colour.
Color _budsTint(NotchController c, NotchActivity a) => switch (NotchController.budsModel(a.title, c.settings.budsColor)) {
  'pixel_peony' => const Color(0xFFE5677F),
  'pixel_wintergreen' => const Color(0xFFB9D3B4),
  'pixel_hazel' => const Color(0xFF9A9D96),
  _ => Colors.white,
};

/// The earbuds' bubble beside the camera: just the buds.
class _BudsBubble extends StatelessWidget {
  final NotchController c;
  final NotchActivity a;
  final double size;
  const _BudsBubble(this.c, this.a, this.size);

  @override
  Widget build(BuildContext context) => Container(
    decoration: const BoxDecoration(color: Colors.black, shape: BoxShape.circle),
    alignment: Alignment.center,
    child: _BudsStill(c, a, size * 0.7),
  );
}

/// The card's picture: the buds come out of their case, once, as it opens (frames rendered from the 3D model, side
/// by side in one picture).
class _BudsOut extends StatefulWidget {
  final NotchController c;
  final NotchActivity a;
  const _BudsOut(this.c, this.a);

  @override
  State<_BudsOut> createState() => _BudsOutState();
}

class _BudsOutState extends State<_BudsOut> with SingleTickerProviderStateMixin {
  late final AnimationController _play = AnimationController(vsync: this, duration: const Duration(milliseconds: 1100));
  ImageStream? _stream;
  ImageStreamListener? _listener;
  ui.Image? _strip;
  String? _model;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _load();
  }

  @override
  void didUpdateWidget(_BudsOut old) {
    super.didUpdateWidget(old);
    _load();
  }

  void _load() {
    final model = NotchController.budsModel(widget.a.title, widget.c.settings.budsColor);
    if (model == _model) return;
    _model = model;
    _stop();
    _strip = null;
    if (model == null) return;
    final stream = AssetImage('assets/buds/${model}_out.webp').resolve(createLocalImageConfiguration(context));
    _listener = ImageStreamListener((info, _) {
      if (!mounted) return;
      setState(() => _strip = info.image);
      _play.forward(from: 0);
    });
    stream.addListener(_listener!);
    _stream = stream;
  }

  void _stop() {
    if (_stream != null && _listener != null) _stream!.removeListener(_listener!);
    _stream = null;
    _listener = null;
  }

  @override
  void dispose() {
    _stop();
    _play.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final strip = _strip;
    if (_model == null) return const Center(child: Icon(Icons.headphones_rounded, color: Colors.white, size: 56));
    if (strip == null) return const SizedBox.shrink();
    return AnimatedBuilder(
      animation: _play,
      builder: (context, _) => CustomPaint(painter: _StripPainter(strip, Curves.easeOut.transform(_play.value))),
    );
  }
}

/// One frame of a strip of square frames, [t] from the first (0) to the last (1).
class _StripPainter extends CustomPainter {
  final ui.Image strip;
  final double t;
  _StripPainter(this.strip, this.t);

  @override
  void paint(Canvas canvas, Size size) {
    final side = strip.height.toDouble();
    final frames = math.max(1, (strip.width / side).floor());
    final i = (t * (frames - 1)).round().clamp(0, frames - 1);
    canvas.drawImageRect(
      strip,
      Rect.fromLTWH(i * side, 0, side, side),
      Offset.zero & size,
      Paint()..filterQuality = FilterQuality.medium,
    );
  }

  @override
  bool shouldRepaint(_StripPainter old) => old.t != t || old.strip != strip;
}

/// One bud's (or the case's) battery: a ring with its name and level.
class _BudLevel extends StatelessWidget {
  final String name;
  final int level;
  final bool nothing;
  const _BudLevel(this.name, this.level, {required this.nothing});

  @override
  Widget build(BuildContext context) {
    final color = level <= 15 ? const Color(0xFFFF453A) : level <= 30 ? const Color(0xFFFF9F0A) : const Color(0xFF30D158);
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        SizedBox(
          width: 34,
          height: 34,
          child: Stack(
            alignment: Alignment.center,
            children: [
              Positioned.fill(
                child: CircularProgressIndicator(
                  value: level / 100,
                  strokeWidth: 3,
                  color: nothing ? _nothingRed : color,
                  backgroundColor: Colors.white12,
                  strokeCap: StrokeCap.round,
                ),
              ),
              Text(name, style: TextStyle(color: NotchPalette.of(context).ink, fontSize: name.length > 1 ? 9.5 : 12, fontWeight: FontWeight.w600)),
            ],
          ),
        ),
        const SizedBox(height: 4),
        Text('$level%', style: TextStyle(color: NotchPalette.of(context).dim, fontSize: 11.5, fontFeatures: const [FontFeature.tabularFigures()])),
      ],
    );
  }
}

class _HolmMark extends StatelessWidget {
  final double size;
  const _HolmMark(this.size);

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(size * 0.27)),
    alignment: Alignment.center,
    child: Container(
      width: size * 0.62,
      height: size * 0.26,
      decoration: BoxDecoration(color: Colors.black, borderRadius: BorderRadius.circular(size * 0.13)),
    ),
  );
}

class _Initials extends StatelessWidget {
  final String name;
  final double size;
  const _Initials(this.name, this.size);

  @override
  Widget build(BuildContext context) {
    final parts = name.trim().split(RegExp(r'\s+')).where((p) => p.isNotEmpty).toList();
    final initials = parts.isEmpty ? '?' : parts.take(2).map((p) => p.characters.first.toUpperCase()).join();
    return Container(
      width: size,
      height: size,
      decoration: const BoxDecoration(
        shape: BoxShape.circle,
        gradient: LinearGradient(colors: [Color(0xFF2E7BFF), Color(0xFF7B4DFF)]),
      ),
      alignment: Alignment.center,
      child: Text(
        initials,
        style: TextStyle(color: Colors.white, fontSize: size * 0.38, fontWeight: FontWeight.w600),
      ),
    );
  }
}

class _Dot extends StatelessWidget {
  final Color color;
  final double size;
  const _Dot({required this.color, required this.size});
  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(color: color, shape: BoxShape.circle),
  );
}

class _RedDot extends StatelessWidget {
  const _RedDot();
  @override
  Widget build(BuildContext context) => Container(
    width: 7,
    height: 7,
    decoration: const BoxDecoration(color: _nothingRed, shape: BoxShape.circle),
  );
}

/// A progress bar: Holm's rounded line, or Nothing's row of dots.
class _Bar extends StatelessWidget {
  final double? progress;
  final Color color;
  final bool nothing;
  const _Bar({required this.progress, required this.color, required this.nothing});

  @override
  Widget build(BuildContext context) {
    if (nothing) {
      return LayoutBuilder(
        builder: (context, box) {
          final n = (box.maxWidth / 7).floor().clamp(1, 80);
          final lit = ((progress ?? 0) * n).round();
          return Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              for (var i = 0; i < n; i++)
                Container(
                  width: 4,
                  height: 4,
                  decoration: BoxDecoration(shape: BoxShape.circle, color: i < lit ? color : NotchPalette.of(context).ink.withValues(alpha: 0.15)),
                ),
            ],
          );
        },
      );
    }
    return ClipRRect(
      borderRadius: BorderRadius.circular(3),
      child: LinearProgressIndicator(value: progress, minHeight: 5, color: color, backgroundColor: NotchPalette.of(context).ink.withValues(alpha: 0.16)),
    );
  }
}

class _Ring extends StatelessWidget {
  final double? progress;
  final Color color;
  final double size;
  const _Ring({required this.progress, required this.color, required this.size});

  @override
  Widget build(BuildContext context) => SizedBox(
    width: size,
    height: size,
    child: CircularProgressIndicator(
      value: progress,
      strokeWidth: size * 0.14,
      color: color,
      backgroundColor: Colors.white.withValues(alpha: 0.18),
    ),
  );
}

/// The equalizer of Holm's closed notch: bars that move while music plays and rest when it pauses.
class _Equalizer extends StatefulWidget {
  final Color color;
  final bool playing;
  final double height;
  const _Equalizer({required this.color, required this.playing, required this.height});

  @override
  State<_Equalizer> createState() => _EqualizerState();
}

class _EqualizerState extends State<_Equalizer> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(seconds: 2));

  @override
  void initState() {
    super.initState();
    if (widget.playing) _c.repeat();
  }

  @override
  void didUpdateWidget(_Equalizer old) {
    super.didUpdateWidget(old);
    if (widget.playing && !_c.isAnimating) _c.repeat();
    if (!widget.playing) _c.stop();
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: _c,
    builder: (context, _) => Row(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        for (var i = 0; i < 4; i++)
          Container(
            margin: EdgeInsets.symmetric(horizontal: widget.height * 0.07),
            width: widget.height * 0.16,
            height:
                widget.height *
                (widget.playing ? 0.3 + 0.7 * (0.5 + 0.5 * math.sin(_c.value * math.pi * 2 * (1.6 + i * 0.7) + i * 1.3)).abs() : 0.25),
            decoration: BoxDecoration(color: widget.color, borderRadius: BorderRadius.circular(widget.height)),
          ),
      ],
    ),
  );
}

String _two(int n) => n.toString().padLeft(2, '0');

String _mmss(int ms) {
  final s = (ms / 1000).floor().clamp(0, 1 << 30);
  return s >= 3600 ? '${s ~/ 3600}:${_two(s % 3600 ~/ 60)}:${_two(s % 60)}' : '${s ~/ 60}:${_two(s % 60)}';
}

String _clock(NotchActivity a) {
  final now = DateTime.now();
  if (a.countdownEnd != null) return _mmss(a.countdownEnd!.difference(now).inMilliseconds + 999);
  if (a.countUpStart != null) return _mmss(now.difference(a.countUpStart!).inMilliseconds);
  return a.text ?? '';
}

/// How long ago, in the phone's language (as Android and the apps say it): "jetzt", "vor 3 Min.", "now", "3 min ago".
String _ago(DateTime t) {
  final s = DateTime.now().difference(t).inSeconds;
  final m = s ~/ 60, h = s ~/ 3600;
  return switch (ui.PlatformDispatcher.instance.locale.languageCode) {
    'de' => s < 60 ? 'jetzt' : (s < 3600 ? 'vor $m Min.' : 'vor $h Std.'),
    'fr' => s < 60 ? 'maintenant' : (s < 3600 ? 'il y a $m min' : 'il y a $h h'),
    'it' => s < 60 ? 'ora' : (s < 3600 ? '$m min fa' : '$h h fa'),
    'es' => s < 60 ? 'ahora' : (s < 3600 ? 'hace $m min' : 'hace $h h'),
    'nl' => s < 60 ? 'nu' : (s < 3600 ? '$m min geleden' : '$h u geleden'),
    _ => s < 60 ? 'now' : (s < 3600 ? '$m min ago' : '$h h ago'),
  };
}

String _summary(NotchActivity a) => switch (a.kind) {
  ActivityKind.music => [a.title, a.text].whereType<String>().join(' · '),
  ActivityKind.timer => '${a.title ?? 'Timer'} · ${_clock(a)}',
  ActivityKind.charging => '${a.title} · ${a.level ?? ''}%',
  ActivityKind.buds => [a.title, if (a.level != null) '${a.level}%'].whereType<String>().join(' · '),
  _ => [a.title, if (a.progress != null) '${(a.progress! * 100).round()}%' else a.text].whereType<String>().join(' · '),
};
