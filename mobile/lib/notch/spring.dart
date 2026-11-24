import 'dart:math' as math;

/// A damped harmonic oscillator, as Holm's notch moves on the desktop (Holm.Core/Spring.cs, SpringValue): [response]
/// is the period in seconds, [damping] 1 glides in, below 1 it swings past and settles back. Solved analytically, so
/// any frame rate gives the same motion.
class Spring {
  double value, velocity = 0, target;
  double response = 0.36, damping = 0.64;
  final double positionTolerance, velocityTolerance;
  bool settled = true;

  Spring(double initial, {this.positionTolerance = 0.25, this.velocityTolerance = 3.0})
      : value = initial,
        target = initial;

  void snap(double v) {
    value = target = v;
    velocity = 0;
    settled = true;
  }

  void retarget(double t, double response, double damping) {
    target = t;
    this.response = math.max(response, 0.05);
    this.damping = damping.clamp(0.25, 1.5);
    settled = (value - target).abs() <= positionTolerance && velocity.abs() <= velocityTolerance;
    if (settled) {
      value = target;
      velocity = 0;
    }
  }

  /// Advances by [dt] seconds; true once settled.
  bool step(double dt) {
    if (settled || dt <= 0) return settled;
    final omega = 2 * math.pi / response;
    final y0 = value - target, v0 = velocity;
    double y, v;
    if (damping < 0.9999) {
      final damped = omega * math.sqrt(1 - damping * damping);
      final decay = math.exp(-damping * omega * dt);
      final c2 = (v0 + damping * omega * y0) / damped;
      final cos = math.cos(damped * dt), sin = math.sin(damped * dt);
      y = decay * (y0 * cos + c2 * sin);
      v = decay * ((c2 * damped - y0 * damping * omega) * cos - (y0 * damped + c2 * damping * omega) * sin);
    } else if (damping <= 1.0001) {
      final decay = math.exp(-omega * dt);
      final c2 = v0 + omega * y0;
      y = decay * (y0 + c2 * dt);
      v = decay * (c2 - omega * (y0 + c2 * dt));
    } else {
      final root = math.sqrt(damping * damping - 1);
      final r1 = -omega * (damping - root), r2 = -omega * (damping + root);
      final a = (v0 - r2 * y0) / (r1 - r2), b = y0 - a;
      final e1 = math.exp(r1 * dt), e2 = math.exp(r2 * dt);
      y = a * e1 + b * e2;
      v = a * r1 * e1 + b * r2 * e2;
    }
    if (y.abs() <= positionTolerance && v.abs() <= velocityTolerance) {
      value = target;
      velocity = 0;
      settled = true;
      return true;
    }
    value = target + y;
    velocity = v;
    return false;
  }
}

/// The notch's shape: width, height, corner radius and where it sits, each on its own spring with the desktop's
/// periods (NotchWindow.MoveShapeTo): opening is a little softer on height than on width, closing snaps in.
class ShapeSprings {
  final w = Spring(0), h = Spring(0), r = Spring(0), x = Spring(0), y = Spring(0);
  final opacity = Spring(0, positionTolerance: 0.004, velocityTolerance: 0.02);

  List<Spring> get all => [w, h, r, x, y, opacity];
  bool get settled => all.every((s) => s.settled);

  void snapTo(double width, double height, double radius, double dx, double dy, double alpha) {
    w.snap(width);
    h.snap(height);
    r.snap(radius);
    x.snap(dx);
    y.snap(dy);
    opacity.snap(alpha);
  }

  void moveTo(double width, double height, double radius, double dx, double dy, double alpha, {required bool grow}) {
    // The desktop's periods, a little more damped: on a phone's small notch its bounce looked like too much.
    w.retarget(width, grow ? 0.34 : 0.30, grow ? 0.74 : 0.82);
    h.retarget(height, grow ? 0.38 : 0.33, grow ? 0.72 : 0.85);
    r.retarget(radius, grow ? 0.36 : 0.30, grow ? 0.76 : 0.85);
    x.retarget(dx, 0.34, 1.0);
    y.retarget(dy, 0.34, 1.0);
    opacity.retarget(alpha, 0.24, 1.0);
  }

  bool step(double dt) {
    var done = true;
    for (final s in all) {
      done = s.step(dt) && done;
    }
    return done;
  }
}
