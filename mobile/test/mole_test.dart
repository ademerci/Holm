// Mullvad's mole in the notch: it must be seen above its hole, not only the hole.
import 'dart:ui' as ui;

import 'package:material_ui/material_ui.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:holm_mobile/notch/mole.dart';

void main() {
  testWidgets('the mole comes out of its hole', (tester) async {
    final key = GlobalKey();
    await tester.pumpWidget(Center(
      child: RepaintBoundary(key: key, child: const ColoredBox(color: Colors.black, child: Mole(up: true, size: 64))),
    ));
    for (var i = 0; i < 40; i++) {
      await tester.pump(const Duration(milliseconds: 50));
    }
    final bytes = await tester.runAsync(() async {
      final image = await (key.currentContext!.findRenderObject() as RenderRepaintBoundary).toImage();
      return (await image.toByteData(format: ui.ImageByteFormat.rawRgba))!;
    });
    // Halfway up, in the middle: the mole's fur, not the black behind it.
    final at = (20 * 64 + 32) * 4;
    final r = bytes!.getUint8(at), g = bytes.getUint8(at + 1), b = bytes.getUint8(at + 2);
    expect(r + g + b, greaterThan(150), reason: 'only the hole was drawn');
  });
}
