import 'package:material_ui/material_ui.dart';

import '../app_state.dart';
import 'targets.dart';
import 'theme.dart';
import 'widgets.dart';

/// The clipboard history (as Holm's Clipboard tool), syncing with a laptop, and sending any entry to a device.
class ClipboardPage extends StatelessWidget {
  final AppState state;
  const ClipboardPage(this.state, {super.key});

  Future<SendTarget?> _pickDevice(BuildContext context, {String title = 'Send to'}) =>
      pickTarget(context, state, title: title);

  Future<void> _toggleSync(BuildContext context, bool on) async {
    if (!on) {
      state.setClipboardSync(false);
      return;
    }
    final peer = await _pickDevice(context, title: 'Sync the clipboard with');
    if (peer != null) state.setClipboardSync(true, peer.name);
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: state,
        builder: (context, _) => ListView(
          padding: const EdgeInsets.only(bottom: 24),
          children: [
            PageHeader('Clipboard', 'What you copied, here and on your laptop',
                trailing: state.clipboard.isEmpty
                    ? null
                    : RoundIconButton(Icons.delete_outline_rounded, tooltip: 'Clear', onPressed: state.clearClipboardHistory)),
            HolmCard(
              child: Column(children: [
                Row(children: [
                  const Icon(Icons.sync_rounded, color: HolmColors.accent),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      const Text('Sync with laptop', style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
                      const SizedBox(height: 2),
                      Text(
                        state.clipboardSync
                            ? 'With ${state.syncTarget ?? 'a device'}: what you copy goes over when you open Holm, texts that arrive are copied right away'
                            : 'Copy on one, paste on the other',
                        style: const TextStyle(color: HolmColors.dim, fontSize: 13),
                      ),
                    ]),
                  ),
                  Switch(value: state.clipboardSync, onChanged: (v) => _toggleSync(context, v)),
                ]),
                const SizedBox(height: 12),
                Row(children: [
                  PillButton('Send clipboard now', icon: Icons.content_paste_go_rounded, onPressed: () async {
                    final peer = await _pickDevice(context);
                    if (peer == null) return;
                    await state.syncClipboardNow();
                    if (state.clipboard.isEmpty) return;
                    await state.sendText(peer, state.clipboard.first.text);
                  }),
                ]),
              ]),
            ),
            if (state.transfer != null)
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 8, 20, 0),
                child: Text('${state.transfer!.title} · ${state.transfer!.detail}',
                    style: TextStyle(color: state.transfer!.failed ? HolmColors.red : HolmColors.dim, fontSize: 13)),
              ),
            const SectionLabel('History'),
            if (state.clipboard.isEmpty)
              const EmptyState(Icons.content_paste_rounded, 'Nothing copied yet',
                  'Texts you copy (seen when you open Holm) and texts beamed from your laptop show up here.'),
            for (final item in state.clipboard)
              HolmCard(
                padding: const EdgeInsets.fromLTRB(18, 8, 4, 8),
                onTap: () {
                  state.copy(item.text);
                  ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Copied')));
                },
                child: Row(children: [
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(item.text.replaceAll('\n', ' '),
                          maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 16)),
                      const SizedBox(height: 3),
                      Text(item.from != null ? 'From ${item.from} · ${timeAgo(item.time)}' : timeAgo(item.time),
                          style: const TextStyle(color: HolmColors.dim, fontSize: 12.5)),
                    ]),
                  ),
                  RoundIconButton(Icons.send_outlined, size: 40, tooltip: 'Send to a device', onPressed: () async {
                    final peer = await _pickDevice(context);
                    if (peer != null) await state.sendText(peer, item.text);
                  }),
                  RoundIconButton(Icons.close_rounded, size: 40, tooltip: 'Remove', onPressed: () => state.removeClip(item)),
                ]),
              ),
          ],
        ),
      );
}
