import 'dart:math' as math;
import 'dart:async';
import 'dart:ui' as ui;

import 'package:flutter/services.dart';
import 'package:material_ui/material_ui.dart';

import '../app_state.dart';
import '../notch/activities.dart';
import '../notch/cover.dart';
import '../notch/designs.dart';
import '../notch/link.dart';
import '../notch/notch_view.dart';
import '../notch/settings.dart';
import 'theme.dart';
import 'widgets.dart';

/// The Dynamic Notch: switching it on, fitting it to the phone's camera with a live preview, and what it shows.
class NotchPage extends StatefulWidget {
  final AppState state;
  const NotchPage(this.state, {super.key});

  @override
  State<NotchPage> createState() => _NotchPageState();
}

class _NotchPageState extends State<NotchPage> with WidgetsBindingObserver {
  final _preview = NotchController();
  bool _editOpen = false, _onScreen = false;
  Map<String, Object?> _status = {};
  bool _bluetooth = true;
  String _shizuku = 'missing';
  bool _images = true, _calendar = true, _calls = true;
  Timer? _poll;

  AppState get s => widget.state;
  NotchSettings get n => s.notch;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _preview.setSettings(n);
    _preview.media = const MediaInfo(
      package: 'preview',
      app: 'Music',
      title: 'Holm',
      artist: 'Your music here',
      playing: true,
      position: 42000,
      duration: 200000,
      updated: 0,
      speed: 1,
      canSeek: false,
    );
    _preview.handle({'type': 'preview', 'on': true});
    _sampleCover();
    _refresh();
    _poll = Timer.periodic(const Duration(seconds: 2), (_) => _refresh());
  }

  /// A made-up cover for the preview, so the open player shows its colours.
  Future<void> _sampleCover() async {
    final recorder = ui.PictureRecorder();
    final canvas = Canvas(recorder);
    const rect = Rect.fromLTWH(0, 0, 96, 96);
    canvas.drawRect(rect, Paint()..shader = const LinearGradient(colors: [Color(0xFFFF5E62), Color(0xFF7B4DFF)]).createShader(rect));
    canvas.drawCircle(const Offset(66, 34), 22, Paint()..color = const Color(0xFFFFC43D));
    final image = await recorder.endRecording().toImage(96, 96);
    final png = (await image.toByteData(format: ui.ImageByteFormat.png))!.buffer.asUint8List();
    _preview.art = png;
    _preview.cover = await CoverColors.of(png);
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _poll?.cancel();
    if (_onScreen) NotchLink.preview(false);
    _preview.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState lifecycle) {
    if (lifecycle == AppLifecycleState.resumed) _refresh();
    if (lifecycle == AppLifecycleState.paused && _onScreen) {
      _onScreen = false;
      NotchLink.preview(false);
    }
  }

  Future<void> _refresh() async {
    final status = await NotchLink.status();
    final bt = await NotchLink.bluetoothAllowed();
    final shizuku = status['popupsAllowed'] == true ? _shizuku : await NotchLink.shizuku();
    final images = await NotchLink.hasPermission('images');
    final calendar = await NotchLink.hasPermission('calendar');
    final calls = await NotchLink.hasPermission('calls');
    if (!mounted) return;
    setState(() {
      _status = status;
      _bluetooth = bt;
      _shizuku = shizuku;
      _images = images;
      _calendar = calendar;
      _calls = calls;
    });
  }

  Future<void> _unlockWithShizuku() async {
    final answer = await NotchLink.unlockWithShizuku();
    if (!mounted) return;
    final text = switch (answer) {
      'granted' => 'Unlocked. Android’s pop-ups can be hidden now.',
      'denied' => 'Shizuku didn’t allow Holm.',
      'missing' => 'Install Shizuku first.',
      'stopped' => 'Start Shizuku first (in the Shizuku app).',
      _ => 'That didn’t work. Try the command from a computer.',
    };
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));
    if (answer == 'granted') await NotchLink.hidePopups(true);
    _refresh();
  }

  void _set(NotchSettings next) {
    s.setNotch(next);
    _preview.setSettings(next);
    setState(() {});
  }

  /// Puts the closed notch right over the camera's cut-out, as Android reports it, a little taller than the camera.
  void _fitToCamera() {
    final cam = Map<String, Object?>.from(_status['camera'] as Map);
    double v(String k) => (cam[k] as num).toDouble();
    final h = (v('h') + 8).clamp(24.0, 80.0);
    final closed = n.closed.copyWith(
      height: h,
      radius: h / 2,
      width: math.max(n.closed.width < 200 ? n.closed.width : 124.0, v('w') + h * 2),
      x: v('x').roundToDouble(),
      y: (v('y') - h / 2).clamp(0.0, 80.0).roundToDouble(),
    );
    _set(n.copyWith(closed: closed, open: n.open.copyWith(x: closed.x)));
    ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('The closed notch now sits over the camera')));
  }

  final Map<String, String> _appNames = {};

  Future<void> _pickApp() async {
    final apps = await NotchLink.apps();
    if (!mounted) return;
    for (final a in apps) {
      _appNames[a.package] = a.name;
    }
    final picked = await showModalBottomSheet<({String package, String name, Uint8List? icon})>(
      context: context,
      isScrollControlled: true,
      builder: (context) => DraggableScrollableSheet(
        expand: false,
        initialChildSize: 0.7,
        builder: (context, scroll) => ListView(
          controller: scroll,
          children: [
            const Padding(
              padding: EdgeInsets.fromLTRB(20, 4, 20, 8),
              child: Text('Which app?', style: TextStyle(fontSize: 22, fontWeight: FontWeight.w600)),
            ),
            for (final a in apps)
              ListTile(
                leading: a.icon == null ? const Icon(Icons.apps_rounded) : Image.memory(a.icon!, width: 32, height: 32),
                title: Text(a.name),
                onTap: () => Navigator.pop(context, a),
              ),
          ],
        ),
      ),
    );
    if (picked != null) await _pickColor(picked.package, picked.name);
  }

  static const _swatches = [
    0xFF0A84FF, 0xFF30D158, 0xFFFF9F0A, 0xFFFF453A, 0xFFFF375F, 0xFFBF5AF2,
    0xFF7B4DFF, 0xFF64D2FF, 0xFFFFD60A, 0xFFAC8E68, 0xFFEBEBF0, 0xFF1ED760,
  ];

  Future<void> _pickColor(String package, String name) async {
    final color = await showModalBottomSheet<int>(
      context: context,
      builder: (context) => Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(name, style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w600)),
            const SizedBox(height: 4),
            const Text('Its colour in the notch', style: TextStyle(color: HolmColors.dim)),
            const SizedBox(height: 16),
            Wrap(
              spacing: 12,
              runSpacing: 12,
              children: [
                for (final c in _swatches)
                  GestureDetector(
                    onTap: () => Navigator.pop(context, c),
                    child: Container(
                      width: 44,
                      height: 44,
                      decoration: BoxDecoration(
                        color: Color(c),
                        shape: BoxShape.circle,
                        border: Border.all(color: n.colors[package] == c ? Colors.white : Colors.transparent, width: 3),
                      ),
                    ),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
    if (color == null) return;
    _appNames[package] = name;
    _set(n.copyWith(colors: {...n.colors, package: color}));
  }

  void _geometry(NotchGeometry g) => _set(_editOpen ? n.copyWith(open: g) : n.copyWith(closed: g));

  void _showOpen(bool open) {
    setState(() => _editOpen = open);
    open ? _preview.expand() : _preview.collapse();
    if (_onScreen) NotchLink.preview(true, open: open);
  }

  @override
  Widget build(BuildContext context) {
    final g = _editOpen ? n.open : n.closed;
    final overlay = _status['overlay'] == true, notifications = _status['notifications'] == true;
    return ListView(
      padding: const EdgeInsets.only(bottom: 28),
      children: [
        const PageHeader('Notch', 'Live activities around the camera'),
        _Permission(
          icon: Icons.layers_outlined,
          title: 'Dynamic Notch',
          detail: overlay ? 'On. It shows over every app.' : 'Turn on “Holm Dynamic Notch” under Accessibility › Installed apps.',
          on: overlay,
          onTap: NotchLink.openAccessibility,
        ),
        _Permission(
          icon: Icons.notifications_none_rounded,
          title: 'Notifications and music',
          detail: notifications
              ? 'On. Music, timers, downloads and new notifications.'
              : 'Let Holm read notifications to show them and the music.',
          on: notifications,
          onTap: NotchLink.openNotificationAccess,
        ),
        _Popups(
          allowed: _status['popupsAllowed'] == true,
          hidden: _status['popupsHidden'] == true,
          shizuku: _shizuku,
          onChanged: (v) async {
            await NotchLink.hidePopups(v);
            _refresh();
          },
          onShizuku: _unlockWithShizuku,
        ),
        if (!_images && n.shows(ActivityKind.screenshot))
          _Permission(
            icon: Icons.screenshot_outlined,
            title: 'Screenshots to your PC',
            detail: 'Let Holm see new screenshots, to offer sending them.',
            on: false,
            onTap: () async {
              await NotchLink.askPermission('images');
              _refresh();
            },
          ),
        if (!_calls && n.shows(ActivityKind.call))
          _Permission(
            icon: Icons.call_outlined,
            title: 'Answer calls in the notch',
            detail: 'Let Holm answer, decline and hang up when you tap there.',
            on: false,
            onTap: () async {
              await NotchLink.askPermission('calls');
              _refresh();
            },
          ),
        if (!_calendar && n.shows(ActivityKind.home))
          _Permission(
            icon: Icons.event_outlined,
            title: 'Next event in the overview',
            detail: 'Let Holm read your calendar to show what’s next.',
            on: false,
            onTap: () async {
              await NotchLink.askPermission('calendar');
              _refresh();
            },
          ),
        if (!_bluetooth)
          _Permission(
            icon: Icons.bluetooth_rounded,
            title: 'Bluetooth names',
            detail: 'Show which headphones connect.',
            on: false,
            onTap: NotchLink.askBluetooth,
          ),
        SectionLabel(
          'Preview',
          trailing: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Text('On screen', style: TextStyle(color: HolmColors.dim, fontSize: 13)),
              Switch(
                value: _onScreen,
                onChanged: overlay
                    ? (v) {
                        setState(() => _onScreen = v);
                        NotchLink.preview(v, open: _editOpen);
                      }
                    : null,
              ),
            ],
          ),
        ),
        _PhoneTop(controller: _preview, settings: n),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 14, 16, 4),
          child: SegmentedButton<bool>(
            segments: const [
              ButtonSegment(value: false, label: Text('Closed'), icon: Icon(Icons.horizontal_rule_rounded)),
              ButtonSegment(value: true, label: Text('Open'), icon: Icon(Icons.crop_landscape_rounded)),
            ],
            selected: {_editOpen},
            onSelectionChanged: (v) => _showOpen(v.first),
          ),
        ),
        _Slider('Width', g.width, 40, 420, (v) => _geometry(g.copyWith(width: v))),
        // Open, the notch is as tall as what it shows needs, up to this.
        _Slider(_editOpen ? 'Max height' : 'Height', g.height, 20, _editOpen ? 320 : 80, (v) => _geometry(g.copyWith(height: v))),
        if (!_editOpen || g.corners == null)
          _Slider('Corner radius', g.radius, 0, 80, (v) => _geometry(g.copyWith(radius: v))),
        if (_editOpen)
          SwitchListTile(
            contentPadding: const EdgeInsets.fromLTRB(20, 0, 12, 0),
            dense: true,
            title: const Text('Each corner on its own', style: TextStyle(color: HolmColors.dim, fontSize: 14)),
            value: g.corners != null,
            onChanged: (on) => _geometry(g.copyWith(corners: on ? List<double>.filled(4, g.radius) : null)),
          ),
        if (_editOpen && g.corners != null)
          for (final (i, label) in const ['Top left', 'Top right', 'Bottom right', 'Bottom left'].indexed)
            _Slider(label, g.corners![i], 0, 80, (v) => _geometry(g.copyWith(corners: [...g.corners!]..[i] = v))),
        _Slider('Left and right', g.x, -160, 160, (v) => _geometry(g.copyWith(x: v))),
        _Slider('From the top', g.y, 0, 80, (v) => _geometry(g.copyWith(y: v))),
        const SectionLabel('Show'),
        _Choice<int>(
          title: 'Activities at once',
          detail: 'Two puts the second in a bubble beside the notch',
          value: n.maxActivities,
          options: const {1: 'One', 2: 'Two'},
          onChanged: (v) => _set(n.copyWith(maxActivities: v)),
        ),
        _Choice<NotchLook>(
          title: 'Open look',
          detail: 'Closed, the notch is always black like the camera',
          value: n.look,
          options: const {NotchLook.holm: 'Holm', NotchLook.nothing: 'Nothing'},
          onChanged: (v) => _set(n.copyWith(look: v)),
        ),
        if (n.look == NotchLook.holm) _Designs(selected: n.design, onChanged: (id) => _set(n.copyWith(design: id))),
        const SectionLabel('Quiet'),
        HolmCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text('Keep quiet', style: TextStyle(fontWeight: FontWeight.w600)),
              const SizedBox(height: 2),
              const Text(
                'Then only calls, timers and alarms show up. What’s already running stays.',
                style: TextStyle(color: HolmColors.dim, fontSize: 12.5),
              ),
              const SizedBox(height: 10),
              SegmentedButton<QuietMode>(
                showSelectedIcon: false,
                segments: [
                  for (final q in QuietMode.values)
                    ButtonSegment(value: q, label: Text(q == QuietMode.dnd ? 'With DND' : q == QuietMode.hours ? 'At times' : 'Never')),
                ],
                selected: {n.quiet},
                onSelectionChanged: (v) => _set(n.copyWith(quiet: v.first)),
              ),
              if (n.quiet == QuietMode.hours) ...[
                const SizedBox(height: 8),
                Row(
                  children: [
                    _TimeButton('From', n.quietFrom, (m) => _set(n.copyWith(quietFrom: m))),
                    const SizedBox(width: 8),
                    _TimeButton('To', n.quietTo, (m) => _set(n.copyWith(quietTo: m))),
                  ],
                ),
              ],
            ],
          ),
        ),
        const SectionLabel('Gestures'),
        HolmCard(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Column(
            children: [
              _GestureRow('Double tap', n.doubleTap, (g) => _set(n.copyWith(doubleTap: g))),
              _GestureRow('Long press', n.longPress, (g) => _set(n.copyWith(longPress: g))),
            ],
          ),
        ),
        const SectionLabel('App colours'),
        HolmCard(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Column(
            children: [
              for (final e in n.colors.entries)
                ListTile(
                  dense: true,
                  leading: CircleAvatar(radius: 10, backgroundColor: Color(e.value)),
                  title: Text(n.muted[e.key] ?? _appNames[e.key] ?? e.key, style: const TextStyle(fontSize: 15)),
                  trailing: IconButton(
                    icon: const Icon(Icons.close_rounded, color: HolmColors.dim),
                    tooltip: 'Back to its own colour',
                    onPressed: () => _set(n.copyWith(colors: Map.of(n.colors)..remove(e.key))),
                  ),
                  onTap: () => _pickColor(e.key, _appNames[e.key] ?? e.key),
                ),
              ListTile(
                dense: true,
                leading: const Icon(Icons.add_rounded, color: HolmColors.accent),
                title: const Text('Choose an app’s colour', style: TextStyle(fontSize: 15)),
                onTap: _pickApp,
              ),
            ],
          ),
        ),
        const SectionLabel('Activities'),
        HolmCard(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Column(
            children: [
              for (final kind in ActivityKind.values)
                SwitchListTile(
                  key: ValueKey(kind),
                  dense: true,
                  title: Text(kind.label, style: const TextStyle(fontSize: 15)),
                  subtitle: Text(kind.detail, style: const TextStyle(color: HolmColors.dim, fontSize: 12.5)),
                  value: n.shows(kind),
                  onChanged: (on) => _set(n.copyWith(off: on ? ({...n.off}..remove(kind)) : {...n.off, kind})),
                ),
            ],
          ),
        ),
        const SectionLabel('Notifications'),
        HolmCard(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Column(
            children: [
              SwitchListTile(
                dense: true,
                title: const Text('Also on your PC', style: TextStyle(fontSize: 15)),
                subtitle: const Text(
                  'PCs signed in to your Holm account show notifications, calls, songs and timers from here, end-to-end encrypted',
                  style: TextStyle(color: HolmColors.dim, fontSize: 12.5),
                ),
                value: n.toPc,
                onChanged: (v) => _set(n.copyWith(toPc: v)),
              ),
              SwitchListTile(
                dense: true,
                title: const Text('Android’s own notices', style: TextStyle(fontSize: 15)),
                subtitle: const Text(
                  'Battery saver, USB, system updates and the like',
                  style: TextStyle(color: HolmColors.dim, fontSize: 12.5),
                ),
                value: n.systemNotices,
                onChanged: (v) => _set(n.copyWith(systemNotices: v)),
              ),
              if (n.muted.isEmpty)
                const ListTile(
                  dense: true,
                  title: Text('No apps switched off', style: TextStyle(fontSize: 15)),
                  subtitle: Text(
                    'Tap the bell in an open notification to stop an app showing in the notch.',
                    style: TextStyle(color: HolmColors.dim, fontSize: 12.5),
                  ),
                ),
              for (final e in n.muted.entries)
                ListTile(
                  dense: true,
                  leading: const Icon(Icons.notifications_off_outlined, color: HolmColors.dim),
                  title: Text(e.value, style: const TextStyle(fontSize: 15)),
                  subtitle: const Text('Not shown in the notch', style: TextStyle(color: HolmColors.dim, fontSize: 12.5)),
                  trailing: TextButton(
                    onPressed: () => _set(n.copyWith(muted: Map.of(n.muted)..remove(e.key))),
                    child: const Text('Show again'),
                  ),
                ),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
          child: Align(
            alignment: Alignment.centerLeft,
            child: Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                if (_status['camera'] is Map)
                  PillButton('Fit to camera', icon: Icons.center_focus_strong_outlined, onPressed: _fitToCamera),
                PillButton(
                  'Reset the shape',
                  icon: Icons.restart_alt_rounded,
                  primary: false,
                  onPressed: () {
                    const d = NotchSettings();
                    _set(n.copyWith(closed: d.closed, open: d.open));
                  },
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

/// The top of a phone at the real size (scaled to fit): a status bar, a camera where the closed notch sits, the notch.
class _PhoneTop extends StatelessWidget {
  final NotchController controller;
  final NotchSettings settings;
  const _PhoneTop({required this.controller, required this.settings});

  @override
  Widget build(BuildContext context) {
    final screenW = MediaQuery.sizeOf(context).width;
    final height = settings.open.y + settings.open.height + 30;
    final cam = settings.closed;
    return Container(
      margin: const EdgeInsets.symmetric(horizontal: 16),
      height: (height * (screenW - 32) / screenW).clamp(120.0, 420.0),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(28),
        gradient: const LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [Color(0xFF2B2D42), Color(0xFF1A1B26)],
        ),
        border: Border.all(color: HolmColors.line),
      ),
      clipBehavior: Clip.antiAlias,
      child: FittedBox(
        fit: BoxFit.fitWidth,
        alignment: Alignment.topCenter,
        child: SizedBox(
          width: screenW,
          height: height,
          child: Stack(
            children: [
              Positioned(
                left: 24,
                top: cam.y + cam.height / 2 - 9,
                child: const Text('12:30', style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600)),
              ),
              Positioned(
                right: 24,
                top: cam.y + cam.height / 2 - 9,
                child: const Row(
                  children: [Icon(Icons.wifi_rounded, size: 16), SizedBox(width: 4), Icon(Icons.battery_5_bar_rounded, size: 16)],
                ),
              ),
              // The camera, where the closed notch should cover it.
              Positioned(
                left: screenW / 2 + cam.x - 6,
                top: cam.y + cam.height / 2 - 6,
                child: Container(
                  width: 12,
                  height: 12,
                  decoration: const BoxDecoration(color: Color(0xFF05060A), shape: BoxShape.circle),
                ),
              ),
              Positioned.fill(child: NotchStage(controller)),
            ],
          ),
        ),
      ),
    );
  }
}

/// Android's own pop-ups, hidden while the notch shows notifications. Switching that setting needs a permission an app
/// can't ask for: it is given once with a command on a computer.
class _Popups extends StatelessWidget {
  static const command = 'adb shell pm grant ch.holmnotch.holm_mobile android.permission.WRITE_SECURE_SETTINGS';
  final bool allowed, hidden;
  final String shizuku;
  final ValueChanged<bool> onChanged;
  final VoidCallback onShizuku;
  const _Popups({
    required this.allowed,
    required this.hidden,
    required this.shizuku,
    required this.onChanged,
    required this.onShizuku,
  });

  @override
  Widget build(BuildContext context) => HolmCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(Icons.web_asset_off_outlined, color: allowed && hidden ? HolmColors.green : HolmColors.dim),
            const SizedBox(width: 14),
            const Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Hide Android’s pop-ups', style: TextStyle(fontWeight: FontWeight.w600)),
                  SizedBox(height: 2),
                  Text(
                    'Notifications show in the notch only. They stay in the notification shade.',
                    style: TextStyle(color: HolmColors.dim, fontSize: 13),
                  ),
                ],
              ),
            ),
            Switch(value: allowed && hidden, onChanged: allowed ? onChanged : null),
          ],
        ),
        if (!allowed) ...[
          const SizedBox(height: 12),
          Text(
            switch (shizuku) {
              'missing' => 'Android lets an app do this only with Shizuku, or one command from a computer.',
              'stopped' => 'Start Shizuku in its app, then unlock here.',
              _ => 'Shizuku is running: one tap gives Holm the permission.',
            },
            style: const TextStyle(color: HolmColors.dim, fontSize: 13),
          ),
          const SizedBox(height: 8),
          PillButton('Unlock with Shizuku', icon: Icons.lock_open_rounded, onPressed: shizuku == 'missing' ? null : onShizuku),
          const SizedBox(height: 12),
          const Text(
            'Or from a computer, with USB debugging on:',
            style: TextStyle(color: HolmColors.dim, fontSize: 13),
          ),
          const SizedBox(height: 8),
          GestureDetector(
            onTap: () {
              Clipboard.setData(const ClipboardData(text: command));
              ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Command copied')));
            },
            child: Container(
              width: double.infinity,
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(color: HolmColors.background, borderRadius: BorderRadius.circular(10)),
              child: const Text(command, style: TextStyle(fontFamily: 'monospace', fontSize: 12)),
            ),
          ),
        ],
      ],
    ),
  );
}

class _Permission extends StatelessWidget {
  final IconData icon;
  final String title, detail;
  final bool on;
  final VoidCallback onTap;
  const _Permission({required this.icon, required this.title, required this.detail, required this.on, required this.onTap});

  @override
  Widget build(BuildContext context) => HolmCard(
    onTap: onTap,
    child: Row(
      children: [
        Icon(icon, color: on ? HolmColors.green : HolmColors.dim),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: const TextStyle(fontWeight: FontWeight.w600)),
              const SizedBox(height: 2),
              Text(detail, style: const TextStyle(color: HolmColors.dim, fontSize: 13)),
            ],
          ),
        ),
        const SizedBox(width: 8),
        on
            ? const Icon(Icons.check_circle_rounded, color: HolmColors.green)
            : const Text(
                'Turn on',
                style: TextStyle(color: HolmColors.accent, fontWeight: FontWeight.w600),
              ),
      ],
    ),
  );
}

class _Slider extends StatelessWidget {
  final String label;
  final double value, min, max;
  final ValueChanged<double> onChanged;
  const _Slider(this.label, this.value, this.min, this.max, this.onChanged);

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(20, 4, 12, 0),
    child: Row(
      children: [
        SizedBox(
          width: 112,
          child: Text(label, style: const TextStyle(color: HolmColors.dim, fontSize: 14)),
        ),
        Expanded(
          child: Slider(value: value.clamp(min, max), min: min, max: max, onChanged: (v) => onChanged(v.roundToDouble())),
        ),
        SizedBox(
          width: 40,
          child: Text('${value.round()}', textAlign: TextAlign.right, style: const TextStyle(fontSize: 13)),
        ),
      ],
    ),
  );
}

class _Choice<T> extends StatelessWidget {
  final String title, detail;
  final T value;
  final Map<T, String> options;
  final ValueChanged<T> onChanged;
  const _Choice({required this.title, required this.detail, required this.value, required this.options, required this.onChanged});

  @override
  Widget build(BuildContext context) => HolmCard(
    child: Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: const TextStyle(fontWeight: FontWeight.w600)),
              const SizedBox(height: 2),
              Text(detail, style: const TextStyle(color: HolmColors.dim, fontSize: 12.5)),
            ],
          ),
        ),
        const SizedBox(width: 10),
        SegmentedButton<T>(
          showSelectedIcon: false,
          segments: [for (final e in options.entries) ButtonSegment(value: e.key, label: Text(e.value))],
          selected: {value},
          onSelectionChanged: (v) => onChanged(v.first),
        ),
      ],
    ),
  );
}

/// The designs of the open notch: Holm's own, then the desktop's, each as a small swatch of its colours.
class _Designs extends StatelessWidget {
  final String? selected;
  final ValueChanged<String?> onChanged;
  const _Designs({required this.selected, required this.onChanged});

  @override
  Widget build(BuildContext context) => SizedBox(
    height: 92,
    child: ListView(
      scrollDirection: Axis.horizontal,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      children: [
        _swatch(null, 'Holm', Colors.black, Colors.white, const Color(0xFF0A84FF)),
        for (final d in NotchDesign.all) _swatch(d.id, d.name, d.background, d.ink, d.accent),
      ],
    ),
  );

  Widget _swatch(String? id, String name, Color bg, Color ink, Color accent) {
    final on = selected == id;
    return GestureDetector(
      onTap: () => onChanged(id),
      child: Padding(
        padding: const EdgeInsets.only(right: 10),
        child: Column(
          children: [
            Container(
              width: 64,
              height: 48,
              decoration: BoxDecoration(
                color: bg,
                borderRadius: BorderRadius.circular(14),
                border: Border.all(color: on ? HolmColors.accent : HolmColors.line, width: on ? 2.5 : 1),
              ),
              padding: const EdgeInsets.all(8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Container(width: 30, height: 5, decoration: BoxDecoration(color: ink, borderRadius: BorderRadius.circular(3))),
                  const SizedBox(height: 5),
                  Container(width: 20, height: 5, decoration: BoxDecoration(color: ink.withAlpha(0x88), borderRadius: BorderRadius.circular(3))),
                  const Spacer(),
                  Container(width: 14, height: 6, decoration: BoxDecoration(color: accent, borderRadius: BorderRadius.circular(3))),
                ],
              ),
            ),
            const SizedBox(height: 4),
            SizedBox(
              width: 68,
              child: Text(name, maxLines: 1, overflow: TextOverflow.ellipsis, textAlign: TextAlign.center, style: const TextStyle(fontSize: 11.5, color: HolmColors.dim)),
            ),
          ],
        ),
      ),
    );
  }
}

class _TimeButton extends StatelessWidget {
  final String label;
  final int minutes;
  final ValueChanged<int> onChanged;
  const _TimeButton(this.label, this.minutes, this.onChanged);

  @override
  Widget build(BuildContext context) => PillButton(
    '$label ${(minutes ~/ 60).toString().padLeft(2, '0')}:${(minutes % 60).toString().padLeft(2, '0')}',
    primary: false,
    icon: Icons.schedule_rounded,
    onPressed: () async {
      final t = await showTimePicker(context: context, initialTime: TimeOfDay(hour: minutes ~/ 60, minute: minutes % 60));
      if (t != null) onChanged(t.hour * 60 + t.minute);
    },
  );
}

class _GestureRow extends StatelessWidget {
  final String label;
  final NotchGesture value;
  final ValueChanged<NotchGesture> onChanged;
  const _GestureRow(this.label, this.value, this.onChanged);

  @override
  Widget build(BuildContext context) => ListTile(
    dense: true,
    title: Text(label, style: const TextStyle(fontSize: 15)),
    trailing: DropdownButton<NotchGesture>(
      value: value,
      underline: const SizedBox.shrink(),
      items: [for (final g in NotchGesture.values) DropdownMenuItem(value: g, child: Text(g.label))],
      onChanged: (g) {
        if (g != null) onChanged(g);
      },
    ),
  );
}
