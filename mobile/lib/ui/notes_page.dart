import 'package:material_ui/material_ui.dart';
import 'package:share_plus/share_plus.dart';

import '../app_state.dart';
import 'targets.dart';
import 'theme.dart';
import 'widgets.dart';

/// The note, as Holm's notepad in the open notch: a small grey label, one dark field with a thin blue edge while
/// typing, kept as you type.
class NotesPage extends StatefulWidget {
  final AppState state;
  const NotesPage(this.state, {super.key});

  @override
  State<NotesPage> createState() => _NotesPageState();
}

class _NotesPageState extends State<NotesPage> {
  late final TextEditingController _text = TextEditingController(text: widget.state.note);
  final _focus = FocusNode();

  AppState get s => widget.state;

  @override
  void initState() {
    super.initState();
    _focus.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _text.dispose();
    _focus.dispose();
    super.dispose();
  }

  void _snack(String text) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));

  Future<void> _send() async {
    if (_text.text.trim().isEmpty) return;
    final peer = await pickTarget(context, s);
    if (peer == null) return;
    final ok = await s.sendText(peer, _text.text);
    if (mounted) _snack(ok ? 'On the clipboard of ${peer.name}' : 'Not sent');
  }

  @override
  Widget build(BuildContext context) {
    final focused = _focus.hasFocus;
    final empty = _text.text.trim().isEmpty;
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 12),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        const Padding(
          padding: EdgeInsets.only(left: 2, bottom: 8),
          child: Text('Notes', style: TextStyle(fontSize: 15, color: HolmColors.dim)),
        ),
        Expanded(
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 150),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(16),
              gradient: const LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                colors: [Color(0xFF202226), Color(0xFF16171A)],
              ),
              border: Border.all(color: focused ? const Color(0xFF1E62C4) : HolmColors.line, width: focused ? 1.5 : 1),
            ),
            child: TextField(
              controller: _text,
              focusNode: _focus,
              maxLines: null,
              expands: true,
              textAlignVertical: TextAlignVertical.top,
              textCapitalization: TextCapitalization.sentences,
              cursorColor: HolmColors.accent,
              style: const TextStyle(fontSize: 16.5, height: 1.4, color: HolmColors.ink),
              onChanged: (v) {
                s.setNote(v);
                setState(() {});
              },
              decoration: const InputDecoration(
                filled: false,
                border: InputBorder.none,
                hintText: 'Jot something down – it stays here, even after a restart',
                hintMaxLines: 3,
                hintStyle: TextStyle(color: Color(0xFF7C7C80), fontSize: 16.5),
                contentPadding: EdgeInsets.fromLTRB(16, 14, 16, 14),
              ),
            ),
          ),
        ),
        const SizedBox(height: 12),
        const Divider(),
        const SizedBox(height: 6),
        Row(children: [
          Text('${_text.text.length} / ${AppState.maxNote}', style: const TextStyle(color: HolmColors.faint, fontSize: 12.5)),
          const Spacer(),
          RoundIconButton(Icons.send_outlined, size: 40, tooltip: 'Send to laptop', onPressed: empty ? null : _send),
          RoundIconButton(Icons.copy_rounded, size: 40, tooltip: 'Copy', onPressed: empty ? null : () {
            s.copy(_text.text);
            _snack('Copied');
          }),
          RoundIconButton(Icons.ios_share_rounded, size: 40, tooltip: 'Share',
              onPressed: empty ? null : () => SharePlus.instance.share(ShareParams(text: _text.text))),
        ]),
      ]),
    );
  }
}
