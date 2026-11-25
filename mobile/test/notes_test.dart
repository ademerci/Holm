import 'package:flutter_test/flutter_test.dart';
import 'package:holm_mobile/app_state.dart';
import 'package:holm_mobile/ui/notes_page.dart';
import 'package:material_ui/material_ui.dart';

class _NotesState extends AppState {
  @override
  void setNote(String text) {
    note = text;
  }
}

void main() {
  testWidgets('pasting beyond the note limit keeps the editor and saved value in sync', (tester) async {
    final state = _NotesState();
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: NotesPage(state))));

    final prefix = List.filled(AppState.maxNote - 1, 'a').join();
    const emoji = '\u{1F469}\u200D\u{1F4BB}';
    final field = find.byType(TextField);
    await tester.enterText(field, '$prefix${emoji}extra');
    await tester.pump();

    expect(tester.widget<TextField>(field).controller!.text, '$prefix$emoji');
    expect(state.note, '$prefix$emoji');
    expect(find.text('${AppState.maxNote} / ${AppState.maxNote}'), findsOneWidget);
  });
}
