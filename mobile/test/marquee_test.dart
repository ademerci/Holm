// Lines in the notch that don't fit: they rest, then slide to show the rest.
import 'package:flutter_test/flutter_test.dart';
import 'package:holm_mobile/notch/marquee.dart';
import 'package:material_ui/material_ui.dart';

void main() {
  Future<double> offsetAfter(WidgetTester tester, String text, Duration wait) async {
    await tester.pumpWidget(Directionality(
      textDirection: TextDirection.ltr,
      child: Center(child: SizedBox(width: 80, child: Marquee(text, style: const TextStyle(fontSize: 14)))),
    ));
    await tester.pump();
    await tester.pump(wait);
    final moved = tester.widgetList<Transform>(find.byType(Transform));
    return moved.isEmpty ? 0 : moved.first.transform.getTranslation().x;
  }

  testWidgets('short text stands still', (tester) async {
    expect(await offsetAfter(tester, 'Hi', const Duration(seconds: 3)), 0);
  });

  testWidgets('long text rests first, then slides', (tester) async {
    const long = 'A message far too long for the notch to show at once';
    expect(await offsetAfter(tester, long, const Duration(milliseconds: 1000)), 0); // still resting
    await tester.pump(const Duration(milliseconds: 1500));
    final x = tester.widget<Transform>(find.byType(Transform).first).transform.getTranslation().x;
    expect(x, lessThan(0)); // sliding towards its end
  });
}
