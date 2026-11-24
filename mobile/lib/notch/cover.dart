import 'dart:async';
import 'dart:math' as math;
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/painting.dart';

/// A cover's colours as Holm's notch shows them on the desktop (Holm.Core/CoverMath.cs): a 24×24 copy blurred three
/// times and a little more saturated, stretched behind the player; a shade over it so white text stays readable;
/// and a bright accent for the equalizer and the progress bar.
class CoverColors {
  final ui.Image backdrop;
  final double shade;
  final Color accent;
  const CoverColors(this.backdrop, this.shade, this.accent);

  static const size = 24;

  static Future<CoverColors?> of(Uint8List png) async {
    try {
      final codec = await ui.instantiateImageCodec(png, targetWidth: size, targetHeight: size);
      final frame = await codec.getNextFrame();
      final data = await frame.image.toByteData(format: ui.ImageByteFormat.rawRgba);
      if (data == null) return null;
      final px = data.buffer.asUint8List();
      final accent = _accent(px);
      final luminance = _backdrop(px);
      return CoverColors(await _decode(px), (1 - 0.22 / math.max(luminance, 0.01)).clamp(0.25, 0.8), accent);
    } catch (_) {
      return null;
    }
  }

  static Future<ui.Image> _decode(Uint8List rgba) {
    final done = Completer<ui.Image>();
    ui.decodeImageFromPixels(rgba, size, size, ui.PixelFormat.rgba8888, done.complete);
    return done.future;
  }

  /// In place: blur (three box blurs ≈ one gaussian) and saturate 1.35×; returns the average luminance.
  static double _backdrop(Uint8List px) {
    const n = size;
    final planes = List.generate(3, (c) => List<double>.generate(n * n, (i) => px[i * 4 + c] / 255.0));
    for (var pass = 0; pass < 3; pass++) {
      for (final plane in planes) {
        _boxBlur(plane, n, true);
        _boxBlur(plane, n, false);
      }
    }
    var luminance = 0.0;
    for (var i = 0; i < n * n; i++) {
      final r = planes[0][i], g = planes[1][i], b = planes[2][i];
      final gray = 0.2126 * r + 0.7152 * g + 0.0722 * b;
      luminance += gray;
      px[i * 4] = _byte(gray + (r - gray) * 1.35);
      px[i * 4 + 1] = _byte(gray + (g - gray) * 1.35);
      px[i * 4 + 2] = _byte(gray + (b - gray) * 1.35);
      px[i * 4 + 3] = 255;
    }
    return luminance / (n * n);
  }

  static int _byte(double v) => (v.clamp(0.0, 1.0) * 255).round();

  static void _boxBlur(List<double> plane, int n, bool horizontal, [int radius = 2]) {
    final line = List<double>.filled(n, 0);
    for (var a = 0; a < n; a++) {
      for (var b = 0; b < n; b++) {
        line[b] = plane[horizontal ? a * n + b : b * n + a];
      }
      for (var b = 0; b < n; b++) {
        var sum = 0.0;
        for (var k = -radius; k <= radius; k++) {
          sum += line[(b + k).clamp(0, n - 1)];
        }
        plane[horizontal ? a * n + b : b * n + a] = sum / (2 * radius + 1);
      }
    }
  }

  static Color _accent(Uint8List px) {
    double r = 0, g = 0, b = 0, total = 0;
    for (var i = 0; i < px.length; i += 4) {
      final R = px[i].toDouble(), G = px[i + 1].toDouble(), B = px[i + 2].toDouble();
      final max = math.max(R, math.max(G, B)), min = math.min(R, math.min(G, B));
      final sat = max <= 0 ? 0.0 : (max - min) / max;
      final weight = 0.02 + sat * sat * (max / 255.0);
      r += R * weight;
      g += G * weight;
      b += B * weight;
      total += weight;
    }
    r /= total;
    g /= total;
    b /= total;
    final vmax = math.max(r, math.max(g, b)), vmin = math.min(r, math.min(g, b));
    final s = vmax <= 0 ? 0.0 : (vmax - vmin) / vmax;
    if (s < 0.18) return const Color(0xFFEBEBF0);
    var hue = vmax == vmin
        ? 0.0
        : vmax == r
            ? 60 * (((g - b) / (vmax - vmin)) % 6)
            : vmax == g
                ? 60 * ((b - r) / (vmax - vmin) + 2)
                : 60 * ((r - g) / (vmax - vmin) + 4);
    if (hue < 0) hue += 360;
    return HSVColor.fromAHSV(1, hue, math.min(s * 1.15, 0.8), 1).toColor();
  }
}
