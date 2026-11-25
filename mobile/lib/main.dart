import 'dart:async';
import 'package:material_ui/material_ui.dart';

import 'app_state.dart';
import 'net/transfer.dart';
import 'notch/link.dart';
import 'ui/targets.dart';
import 'update.dart';
import 'notch/notch_main.dart';
import 'ui/account_page.dart';
import 'ui/chat_page.dart';
import 'ui/clipboard_page.dart';
import 'ui/devices_page.dart';
import 'ui/notch_page.dart';
import 'ui/notes_page.dart';
import 'ui/theme.dart';
import 'ui/widgets.dart';

/// The Dynamic Notch's engine (started by NotchService in the accessibility overlay).
@pragma('vm:entry-point')
Future<void> notchMain() => runNotch();

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final state = AppState();
  await state.load();
  runApp(HolmApp(state));
}

class HolmApp extends StatelessWidget {
  final AppState state;
  const HolmApp(this.state, {super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'Holm',
        debugShowCheckedModeBanner: false,
        theme: holmTheme(),
        darkTheme: holmTheme(),
        themeMode: ThemeMode.dark,
        home: HomeShell(state),
      );
}

/// The app's frame: four pages and the round tool buttons under them, as in Holm's open notch.
class HomeShell extends StatefulWidget {
  final AppState state;
  const HomeShell(this.state, {super.key});

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> with WidgetsBindingObserver {
  int _page = 0;

  AppState get s => widget.state;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    s.askOffer = _askOffer;
    s.toast = (text) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));
    };
    WidgetsBinding.instance.addPostFrameCallback((_) => s.syncClipboardNow());
    Updater.instance.addListener(_whatsNew);
    NotchLink.onShared = _takeShared;
    WidgetsBinding.instance.addPostFrameCallback((_) => _takeShared());
    NotchLink.onOpen = (page) {
      final i = const ['devices', 'chat', 'clipboard', 'notes', 'notch'].indexOf(page);
      if (i >= 0 && mounted) setState(() => _page = i);
    };
  }

  @override
  void dispose() {
    Updater.instance.removeListener(_whatsNew);
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  /// Shared from another app: where it goes, and off it goes.
  Future<void> _takeShared() async {
    final shared = await NotchLink.takeShared();
    if (shared == null || !mounted) return;
    setState(() => _page = 0); // the devices page shows how sending goes
    final target = await pickTarget(context, s, title: shared.uris.isEmpty ? 'Send the text to' : 'Send to');
    if (target == null) return;
    if (shared.uris.isEmpty) {
      await s.sendText(target, shared.text ?? '');
      return;
    }
    final paths = <String>[];
    for (final uri in shared.uris) {
      final path = await NotchLink.copyUri(uri);
      if (path != null) paths.add(path);
    }
    if (paths.isEmpty) {
      s.toast?.call("Couldn't read what was shared");
      return;
    }
    await s.sendFiles(target, paths);
  }

  bool _showingWhatsNew = false;

  /// Once after an update: what's new in this version.
  void _whatsNew() {
    final info = Updater.instance.whatsNew;
    if (info == null || _showingWhatsNew || !mounted) return;
    _showingWhatsNew = true;
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      await showModalBottomSheet<void>(
        context: context,
        builder: (context) => Padding(
          padding: const EdgeInsets.fromLTRB(24, 4, 24, 28),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('New in Holm ${info.name}', style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w600)),
            const SizedBox(height: 12),
            Text(info.notes, style: const TextStyle(color: HolmColors.dim, fontSize: 15, height: 1.4)),
            const SizedBox(height: 20),
            PillButton('Nice', onPressed: () => Navigator.pop(context)),
          ]),
        ),
      );
      Updater.instance.seenWhatsNew();
      _showingWhatsNew = false;
    });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState lifecycle) {
    s.setForeground(lifecycle == AppLifecycleState.resumed);
    if (lifecycle == AppLifecycleState.resumed) {
      // Android lets an app read the clipboard only while it is in front: this is when sync looks.
      s.syncClipboardNow();
      s.nearby.refresh();
      s.startInbox();
      s.announceDevice(); // tells the account's PCs this phone is here again
    }
  }

  Future<bool> _askOffer(ShareOffer offer) async {
    if (!mounted) return false;
    final single = offer.files.length == 1;
    BuildContext? dialog;
    // The sender doesn't wait forever (AppState counts no answer in a minute as no): the question goes with it.
    final expire = Timer(const Duration(seconds: 58), () {
      final open = dialog;
      if (open != null && open.mounted) Navigator.of(open).pop(false);
    });
    final answer = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        dialog = context;
        return AlertDialog(
        icon: Avatar(offer.sender, size: 56, icon: Icons.laptop_rounded),
        title: Text(offer.isText ? 'Text from ${offer.sender}' : '${offer.sender} wants to send you'),
        content: Text(
          offer.isText
              ? 'Copy it to your clipboard?'
              : single
                  ? '${offer.files.first.name} · ${fileSize(offer.totalBytes)}'
                  : '${offer.files.length} files · ${fileSize(offer.totalBytes)}',
          textAlign: TextAlign.center,
          style: const TextStyle(color: HolmColors.dim),
        ),
        actionsAlignment: MainAxisAlignment.center,
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Decline')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: Text(offer.isText ? 'Copy' : 'Accept')),
        ],
      );
      },
    );
    expire.cancel();
    return answer ?? false;
  }

  void _settings() {
    final name = TextEditingController(text: s.deviceName);
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (context) => Padding(
        padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.of(context).viewInsets.bottom + 28),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('Settings', style: TextStyle(fontSize: 24, fontWeight: FontWeight.w600)),
          const SizedBox(height: 16),
          const Text('This phone’s name', style: TextStyle(color: HolmColors.dim)),
          const SizedBox(height: 6),
          TextField(controller: name, maxLength: 40, decoration: const InputDecoration(hintText: 'Alex’s phone')),
          Text(
              s.account == null
                  ? 'Holm on your laptop and your friends see this name.'
                  : 'Your devices see this name. Friends see the name in your Holm profile.',
              style: const TextStyle(color: HolmColors.faint, fontSize: 13)),
          const SizedBox(height: 16),
          HolmCard(
            margin: EdgeInsets.zero,
            onTap: () => showAccount(context, s),
            child: Row(children: [
              const Icon(Icons.person_outline_rounded, color: HolmColors.dim),
              const SizedBox(width: 12),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  const Text('Holm account', style: TextStyle(fontWeight: FontWeight.w600)),
                  Text(s.account?.email ?? 'Not signed in', style: const TextStyle(color: HolmColors.dim, fontSize: 13)),
                ]),
              ),
              const Icon(Icons.chevron_right_rounded, color: HolmColors.faint),
            ]),
          ),
          const SizedBox(height: 12),
          // Updates: the version here, and a check now (Holm also checks by itself every few hours).
          ListenableBuilder(
            listenable: Updater.instance,
            builder: (context, _) {
              final u = Updater.instance;
              final next = u.available;
              final detail = u.checking
                  ? 'Checking…'
                  : next != null
                      ? 'Holm ${next.name} is ready'
                      : u.unreachable
                          ? "Couldn't reach Holm's update server"
                          : u.lastChecked != null
                              ? 'This is the newest version'
                              : 'Holm looks for new versions every few hours';
              return HolmCard(
                margin: EdgeInsets.zero,
                child: Row(children: [
                  const Icon(Icons.system_update_outlined, color: HolmColors.dim),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text('Holm ${u.currentName ?? ''}'.trim(), style: const TextStyle(fontWeight: FontWeight.w600)),
                      Text(detail, style: const TextStyle(color: HolmColors.dim, fontSize: 13)),
                    ]),
                  ),
                  const SizedBox(width: 8),
                  next != null
                      ? PillButton('Update', onPressed: u.installing ? null : u.install)
                      : PillButton('Check', primary: false, onPressed: u.checking ? null : u.check),
                ]),
              );
            },
          ),
          const SizedBox(height: 4),
          StatefulBuilder(
            builder: (context, setSheet) => SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Test versions'),
              subtitle: const Text('Get new versions of Holm before everyone else', style: TextStyle(color: HolmColors.dim, fontSize: 13)),
              value: s.notch.betaUpdates,
              onChanged: (v) {
                s.setNotch(s.notch.copyWith(betaUpdates: v));
                setSheet(() {});
              },
            ),
          ),
          const SizedBox(height: 8),
          Row(children: [
            PillButton('Save', onPressed: () {
              s.setDeviceName(name.text);
              Navigator.pop(context);
            }),
            const SizedBox(width: 8),
            PillButton('My friend code', primary: false, onPressed: () => showMyCode(context, s)),
          ]),
        ]),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final pages = [DevicesPage(s), ChatListPage(s), ClipboardPage(s), NotesPage(s), NotchPage(s)];
    return Scaffold(
      body: SafeArea(
        bottom: false,
        child: Column(children: [
          _TopBar(onSettings: _settings, name: s.deviceName),
          const _UpdateBanner(),
          Expanded(child: IndexedStack(index: _page, children: pages)),
        ]),
      ),
      bottomNavigationBar: ListenableBuilder(
        listenable: s,
        builder: (context, _) => _ToolBar(
          selected: _page,
          unread: s.unreadTotal + s.incoming.length,
          onSelect: (i) => setState(() => _page = i),
        ),
      ),
    );
  }
}

/// A new version of Holm is ready: one button installs it.
class _UpdateBanner extends StatelessWidget {
  const _UpdateBanner();

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: Updater.instance,
        builder: (context, _) {
          final u = Updater.instance;
          final info = u.available;
          if (info == null) return const SizedBox.shrink();
          final detail = u.error ??
              (u.progress != null
                  ? 'Downloading… ${(u.progress! * 100).round()}%'
                  : u.installing
                      ? 'Installing…'
                      : info.notes.isNotEmpty
                          ? info.notes
                          : 'A new version is ready to install.');
          return HolmCard(
            margin: const EdgeInsets.fromLTRB(16, 10, 16, 0),
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              Row(children: [
                const Icon(Icons.system_update_rounded, color: HolmColors.accent),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text('Holm ${info.name} is ready', style: const TextStyle(fontWeight: FontWeight.w600)),
                    const SizedBox(height: 2),
                    Text(detail,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(color: u.error != null ? const Color(0xFFFF6B6B) : HolmColors.dim, fontSize: 13)),
                  ]),
                ),
                const SizedBox(width: 10),
                PillButton('Update', onPressed: u.installing ? null : u.install),
              ]),
              if (u.progress != null) ...[
                const SizedBox(height: 10),
                LinearProgressIndicator(value: u.progress, minHeight: 3, borderRadius: BorderRadius.circular(2)),
              ],
            ]),
          );
        },
      );
}

class _TopBar extends StatelessWidget {
  final VoidCallback onSettings;
  final String name;
  const _TopBar({required this.onSettings, required this.name});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(20, 10, 8, 0),
        child: Row(children: [
          Container(
            width: 34,
            height: 34,
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(9)),
            alignment: Alignment.center,
            child: Container(
              width: 22,
              height: 9,
              decoration: BoxDecoration(color: Colors.black, borderRadius: BorderRadius.circular(5)),
            ),
          ),
          const SizedBox(width: 10),
          const Text('Holm', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
          const Spacer(),
          RoundIconButton(Icons.settings_outlined, tooltip: 'Settings', onPressed: onSettings),
        ]),
      );
}

/// The tool row of Holm's open notch: thin icons, a soft circle behind the selected one, a line above.
class _ToolBar extends StatelessWidget {
  final int selected;
  final int unread;
  final ValueChanged<int> onSelect;
  const _ToolBar({required this.selected, required this.unread, required this.onSelect});

  static const _items = [
    (Icons.devices_outlined, Icons.devices_rounded, 'Devices'),
    (Icons.chat_bubble_outline_rounded, Icons.chat_bubble_rounded, 'Chat'),
    (Icons.content_paste_outlined, Icons.content_paste_rounded, 'Clipboard'),
    (Icons.sticky_note_2_outlined, Icons.sticky_note_2_rounded, 'Notes'),
    (Icons.crop_7_5_outlined, Icons.crop_7_5_rounded, 'Notch'),
  ];

  @override
  Widget build(BuildContext context) => Container(
        decoration: const BoxDecoration(
          color: HolmColors.sidebar,
          border: Border(top: BorderSide(color: HolmColors.line)),
        ),
        child: SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: Row(mainAxisAlignment: MainAxisAlignment.spaceEvenly, children: [
              for (var i = 0; i < _items.length; i++)
                Semantics(
                  button: true,
                  selected: i == selected,
                  label: _items[i].$3,
                  child: InkWell(
                    borderRadius: BorderRadius.circular(30),
                    onTap: () => onSelect(i),
                    child: SizedBox(
                      width: 68,
                      child: Column(mainAxisSize: MainAxisSize.min, children: [
                        Stack(clipBehavior: Clip.none, children: [
                          AnimatedContainer(
                            duration: const Duration(milliseconds: 220),
                            curve: Curves.easeOutBack,
                            width: 56,
                            height: 34,
                            decoration: BoxDecoration(
                              color: i == selected ? HolmColors.selected : Colors.transparent,
                              borderRadius: BorderRadius.circular(17),
                            ),
                            child: Icon(i == selected ? _items[i].$2 : _items[i].$1,
                                size: 23, color: i == selected ? HolmColors.ink : HolmColors.dim),
                          ),
                          if (i == 1 && unread > 0)
                            Positioned(
                              right: 6,
                              top: -2,
                              child: Container(
                                padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
                                decoration: BoxDecoration(color: HolmColors.red, borderRadius: BorderRadius.circular(9)),
                                child: Text('$unread', style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w700)),
                              ),
                            ),
                        ]),
                        const SizedBox(height: 4),
                        Text(_items[i].$3,
                            style: TextStyle(fontSize: 11.5, color: i == selected ? HolmColors.ink : HolmColors.dim)),
                      ]),
                    ),
                  ),
                ),
            ]),
          ),
        ),
      );
}
