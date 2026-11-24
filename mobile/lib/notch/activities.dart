import 'dart:math' as math;
import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/painting.dart';

import 'brands.dart';
import 'cover.dart';
import 'settings.dart';

/// The music that plays (any media session), as the notch shows it.
class MediaInfo {
  final String package, app;
  final String? title, artist;
  final bool playing;
  final int position, duration; // ms
  final int updated; // SystemClock.elapsedRealtime of the position, ms
  final double speed;
  final bool canSeek;
  const MediaInfo({required this.package, required this.app, this.title, this.artist, required this.playing,
      required this.position, required this.duration, required this.updated, required this.speed, required this.canSeek});

  /// Where the song is now, guessed from the last position and how long ago it was (wall clock as a stand-in).
  int positionAt(DateTime received) {
    if (!playing) return position;
    final p = position + (DateTime.now().difference(received).inMilliseconds * speed).round();
    return duration > 0 ? p.clamp(0, duration) : p;
  }
}

/// One live activity: something the notch shows, for a while (an alert) or as long as it lasts (persistent).
class NotchActivity {
  final String id;
  final ActivityKind kind;
  final bool persistent;
  final int priority;
  final DateTime created = DateTime.now();
  DateTime? until;

  String? title, text, label, package, notificationKey, offerId, page;
  /// The notification's own buttons: their index in it and their title (a reply's not among them, see [replyIndex]).
  List<(int, String)> actions = const [];
  /// The notification's reply button, which asks for text: answered right in the notch.
  int? replyIndex;
  /// The notification's own picture (the next turn, the caller), a screenshot's thumbnail.
  Uint8List? picture;
  /// Calls: 1 ringing, 2 talking (Notification.CallStyle).
  int callType = 0;
  /// A screenshot to send to the PC.
  String? uri;
  double? progress; // 0..1; null with [busy] means indeterminate
  bool busy = false, done = false, failed = false, on = true, mullvad = false, preview = false;
  /// A screen recording (Android's own, counting up): a red dot and its time, not a stopwatch.
  bool recording = false;
  int? level; // battery %
  DateTime? countdownEnd, countUpStart;

  NotchActivity(this.id, this.kind, {this.persistent = false, this.priority = 0, Duration? show})
      : until = show == null ? null : DateTime.now().add(show);

  bool get hasProgress => progress != null || busy;
  bool get isAlert => !persistent;
  bool get isOffer => offerId != null;

  /// Notifications, chats and reminders open a banner while closed: their text needs room under the camera.
  bool get banner =>
      isAlert &&
      const {ActivityKind.notification, ActivityKind.chat, ActivityKind.reminder, ActivityKind.clipboard, ActivityKind.screenshot}
          .contains(kind);

  /// Short news (time's up, charging, connections) widens the notch: a name left of the camera, a value right of it.
  bool get wide =>
      (kind == ActivityKind.timer && done) ||
      (kind == ActivityKind.download && done && isAlert) ||
      (isAlert && const {ActivityKind.charging, ActivityKind.wifi, ActivityKind.bluetooth, ActivityKind.vpn, ActivityKind.volume}.contains(kind));

  bool get peeks => banner || wide;
}

/// What the notch knows and shows: fed by the system (notifications, media, battery, connections) and by Holm.
class NotchController extends ChangeNotifier {
  NotchSettings settings = const NotchSettings();
  bool landscape = false;
  bool expanded = false;
  /// The overlay can limit touches to the notch, so its window keeps one size (NotchService.limitTouches).
  bool touchRegion = false;

  final Map<String, NotchActivity> _persistent = {};
  final List<NotchActivity> _alerts = [];
  final Map<String, Uint8List> icons = {};
  final Map<String, NotchActivity> _byKey = {}; // notifications as last seen, to tell a finished download
  final Map<String, String?> _sortKeys = {};

  MediaInfo? media;
  DateTime mediaReceived = DateTime.now();
  Uint8List? art;
  CoverColors? cover;
  String? _artKey;
  DateTime? _pausedAt;
  int? batteryLevel;
  bool plugged = false, _batteryKnown = false, _full = false;
  String? _focus; // the activity the person picked to see first
  final Set<String> _hidden = {}; // ongoing activities swiped away, until they end

  /// Asks native code to do something (media controls, opening a notification, answering an offer); some answer.
  Future<Object?> Function(String method, Object? args)? action;

  /// Do Not Disturb is on (for the quiet mode that follows it).
  bool dnd = false;
  /// Connections, for the PC's phone tab.
  bool wifiOn = false, vpnOn = false;
  String? bluetoothName;
  /// A reply is being typed in the open notch.
  bool typing = false;
  /// Holm's devices that are there now (from the app), for the overview.
  List<String> devices = const [];
  /// The last level of each pair of headphones (name → %).
  final Map<String, int> headphones = {};
  /// The open volume notch: each stream's level (media, ring, notification, alarm, call), 0..1.
  Map<String, double> volumes = const {};

  /// From the overview: the next alarm and the next event.
  DateTime? nextAlarm;
  ({String title, DateTime begin})? nextEvent;

  Timer? _tick;

  NotchController() {
    _tick = Timer.periodic(const Duration(seconds: 1), (_) => _expire());
  }

  @override
  void dispose() {
    _tick?.cancel();
    super.dispose();
  }

  /// The earbuds' 3D picture (assets/buds) for a device name, or null for headphones Holm has no picture of.
  static String? budsModel(String? name, String color) {
    final n = (name ?? '').toLowerCase();
    if (n.contains('airpods')) return 'airpods';
    if (n.contains('pixel buds')) return 'pixel_$color';
    // Nothing's earbuds: "Nothing Ear (2)", "Ear (a)", and CMF's "CMF Buds 2 Pro".
    if (n.contains('nothing') || n.startsWith('ear (') || n.contains('cmf buds')) return 'nothing';
    return null;
  }

  /// Headphones by name, as they connect: something to show the earbuds activity for.
  static bool looksLikeEarbuds(String? name) =>
      RegExp(r'airpods|pixel buds|buds|earbuds|headphones|wf-|wh-|jabra|bose|beats|sennheiser|nothing ear|^ear \(|freebuds',
              caseSensitive: false)
          .hasMatch(name ?? '');

  /// The connected earbuds (null when none): the earbuds activity follows them.
  String? _budsName;

  /// Each earbud's and the case's battery, from Google Play services' card (Fast Pair earbuds), else null.
  int? budsLeft, budsRight, budsCase;

  void _updateBuds() {
    final name = _budsName;
    if (name == null) {
      _persistent.remove('buds');
      return;
    }
    final a = _persistent['buds'] ?? NotchActivity('buds', ActivityKind.buds, persistent: true, priority: -50);
    final both = [budsLeft, budsRight].whereType<int>();
    a
      ..title = name
      // The emptier bud says how long they last; else what Android knows of the pair.
      ..level = both.isNotEmpty ? both.reduce(math.min) : headphones[name]
      ..label = 'Earbuds';
    _persistent['buds'] = a;
  }

  /// The activities to show, the first one in the notch, the second (if two are allowed) in the bubble beside it.
  /// The earbuds always take the bubble when something else shows (small, beside everything else).
  List<NotchActivity> get visible {
    if (landscape) return const [];
    final now = DateTime.now();
    bool wanted(NotchActivity a) =>
        settings.shows(a.kind) && !settings.muted.containsKey(a.package) && !snoozed(a.package) && !_hidden.contains(a.id);
    // The newest first; of two from the same moment, the one that came later (later in the list).
    final order = {for (var i = 0; i < _alerts.length; i++) _alerts[i]: i};
    final alerts = _alerts.where((a) => (a.until == null || a.until!.isAfter(now) || expanded) && wanted(a)).toList()
      ..sort((a, b) {
        final byTime = b.created.compareTo(a.created);
        return byTime != 0 ? byTime : order[b]!.compareTo(order[a]!);
      });
    final persistent = _persistent.values.where(wanted).toList()
      ..sort((a, b) {
        if (a.id == _focus) return -1;
        if (b.id == _focus) return 1;
        return b.priority.compareTo(a.priority);
      });
    final all = [...alerts.take(1), ...persistent, ...alerts.skip(1)];
    final buds = all.where((a) => a.kind == ActivityKind.buds).firstOrNull;
    // The earbuds are the small one beside anything else: in the bubble ([budsBubble]), in the notch only opened from
    // there. Alone, they're the notch's own activity, as big as any.
    final others = all.where((a) => a.kind != ActivityKind.buds).toList();
    _othersShowing = others.length;
    final rest = others.take(settings.maxActivities).toList();
    if (buds != null && (rest.isEmpty || (expanded && _focus == buds.id))) return [buds];
    return rest;
  }

  /// How many other activities there are (all of them, also those beyond the ones shown), as of [visible].
  int _othersShowing = 0;

  /// The earbuds in the bubble beside the camera (beside whatever else shows), while they're connected.
  NotchActivity? get budsBubble {
    if (landscape || primary?.kind == ActivityKind.buds) return null; // alone (or opened), they're in the notch
    if (_othersShowing >= 2) return null; // two others already: the earbuds step back until one is gone
    final a = _persistent.values.where((a) => a.kind == ActivityKind.buds).firstOrNull;
    return a != null && settings.shows(ActivityKind.buds) && !_hidden.contains(a.id) ? a : null;
  }

  /// A tap on the earbuds' bubble: their card opens (the buds come out of the case).
  void openBuds() {
    final a = budsBubble;
    if (a == null) return;
    _focus = a.id;
    expanded = true;
    notifyListeners();
  }

  NotchActivity? get primary {
    final v = visible;
    return v.isEmpty ? null : v.first;
  }

  void setSettings(NotchSettings s) {
    settings = s;
    notifyListeners();
  }

  void expand() {
    if (primary == null || expanded) return;
    expanded = true;
    if (primary!.kind == ActivityKind.volume) _readVolumes();
    notifyListeners();
  }

  Future<void> _readVolumes() async {
    final r = await action?.call('volumes', null);
    if (r is Map) {
      volumes = {for (final e in r.entries) '${e.key}': (e.value as num).toDouble()};
      notifyListeners();
    }
  }

  /// A finger on a volume: [stream] is "media", "ring", … or "keys" (the one the volume keys set, in the pill).
  void setVolume(String stream, double level) {
    level = level.clamp(0.0, 1.0);
    if (stream != 'keys') volumes = {...volumes, stream: level};
    final a = _alerts.where((a) => a.id == 'volume').firstOrNull;
    if (a != null) {
      if (stream == 'keys' || stream == 'media') a.progress = level;
      a.until = DateTime.now().add(const Duration(seconds: 3)); // stays while you set it
    }
    action?.call('setVolume', {'stream': stream, 'level': level});
    notifyListeners();
  }

  void collapse() {
    if (!expanded) return;
    expanded = false;
    if (_persistent[_focus]?.kind == ActivityKind.buds) _focus = null; // back into the bubble
    _endTyping();
    _persistent.remove('home');
    notifyListeners();
  }

  /// While nothing shows: the time, the batteries and what's next, with a tap on the camera.
  void showHome() {
    if (!settings.shows(ActivityKind.home) || landscape) return;
    _persistent['home'] = NotchActivity('home', ActivityKind.home, persistent: true, priority: -100)..label = 'Overview';
    _focus = 'home';
    expanded = true;
    notifyListeners();
    final ask = action?.call('overview', null);
    ask?.then((r) {
      if (r is! Map) return;
      final alarm = r['alarm'] as int?;
      nextAlarm = alarm == null ? null : DateTime.fromMillisecondsSinceEpoch(alarm);
      final ev = r['event'];
      nextEvent = ev is Map && ev['begin'] is int
          ? (title: '${ev['title'] ?? ''}', begin: DateTime.fromMillisecondsSinceEpoch(ev['begin'] as int))
          : null;
      notifyListeners();
    });
  }

  /// A double tap or long press on the closed notch, or on the camera while nothing shows.
  void gesture(NotchGesture g) {
    switch (g) {
      case NotchGesture.none:
        return;
      case NotchGesture.open:
        primary == null ? showHome() : expand();
      default:
        action?.call('gesture', g.name);
    }
  }

  // ───────────── replying ─────────────

  void startTyping(NotchActivity a) {
    if (a.replyIndex == null) return;
    typing = true;
    _focus = a.id;
    a.until = null; // stays while you type
    action?.call('keyboard', true);
    notifyListeners();
  }

  void _endTyping() {
    if (!typing) return;
    typing = false;
    action?.call('keyboard', false);
  }

  void cancelTyping() {
    _endTyping();
    notifyListeners();
  }

  /// Sends [text] as the answer to the notification; true if the app took it.
  Future<bool> reply(NotchActivity a, String text) async {
    if (a.notificationKey == null || a.replyIndex == null || text.trim().isEmpty) return false;
    final ok = await action?.call('reply', {'key': a.notificationKey, 'index': a.replyIndex, 'text': text.trim()}) == true;
    _endTyping();
    if (ok) {
      _alerts.remove(a);
      expanded = false;
    }
    notifyListeners();
    return ok;
  }

  // ───────────── calls ─────────────

  void callAction(NotchActivity a, String which) {
    if (a.notificationKey == null) return;
    action?.call('call', {'key': a.notificationKey, 'action': which});
    if (which != 'answer') expanded = false;
    notifyListeners();
  }

  bool callMuted = false;

  Future<void> muteCall() async {
    callMuted = await action?.call('muteCall', null) == true;
    notifyListeners();
  }

  /// Whether the notch keeps quiet now: only calls, timers and alarms come through.
  bool get quiet => switch (settings.quiet) {
        QuietMode.off => false,
        QuietMode.dnd => dnd,
        QuietMode.hours => settings.inQuietHours(DateTime.now()),
      };

  /// Shows [a] first (a tap on the bubble).
  void focus(NotchActivity a) {
    if (a.isAlert) {
      _alerts.remove(a);
      _alerts.add(a..until = DateTime.now().add(const Duration(seconds: 8)));
    } else {
      _focus = a.id;
    }
    notifyListeners();
  }

  void dismiss(NotchActivity a) {
    _alerts.remove(a);
    if (a.isOffer) _persistent.remove(a.id);
    if (visible.isEmpty) expanded = false;
    notifyListeners();
  }

  /// A swipe sideways: an alert goes; something ongoing (a download, the music) hides until it ends.
  /// To the left the notification stays in the shade, as with Android's own pop-ups; to the right ([clear]) it goes
  /// from there too, if it can.
  void swipeAway(NotchActivity a, {bool clear = false}) {
    if (clear && a.notificationKey != null) action?.call('clear', {'key': a.notificationKey});
    if (a.isAlert) {
      _alerts.remove(a);
    } else if (a.isOffer) {
      answerOffer(a, false);
      return;
    } else {
      _hidden.add(a.id);
    }
    if (visible.isEmpty) expanded = false;
    notifyListeners();
  }

  /// Apps quiet for a while (the snooze button of an open notification), until when.
  final Map<String, DateTime> _snooze = {};

  bool snoozed(String? pkg) {
    final until = pkg == null ? null : _snooze[pkg];
    if (until == null) return false;
    if (until.isAfter(DateTime.now())) return true;
    _snooze.remove(pkg);
    return false;
  }

  /// [a]'s app stays out of the notch for [time] (5 minutes); then it shows again by itself.
  void snooze(NotchActivity a, [Duration time = const Duration(minutes: 5)]) {
    final pkg = a.package;
    if (pkg == null) return;
    _snooze[pkg] = DateTime.now().add(time);
    _alerts.removeWhere((x) => x.package == pkg);
    if (visible.isEmpty) expanded = false;
    notifyListeners();
  }

  /// Whether the notch can stop showing [a]'s app (notifications from an app, not the notch's own activities).
  bool canMute(NotchActivity a) =>
      a.package != null && const {ActivityKind.notification, ActivityKind.reminder}.contains(a.kind);

  /// Never show this app's notifications in the notch again (until switched back on in the settings).
  void mute(NotchActivity a) {
    final pkg = a.package;
    if (pkg == null) return;
    final name = Brand.of(pkg)?.name ?? a.label ?? pkg;
    settings = settings.copyWith(muted: {...settings.muted, pkg: name});
    action?.call('mute', {'package': pkg, 'name': name});
    _alerts.removeWhere((x) => x.package == pkg);
    if (visible.isEmpty) expanded = false;
    notifyListeners();
  }

  void _expire() {
    final now = DateTime.now();
    _tellPcState();
    var changed = false;
    _alerts.removeWhere((a) {
      final gone = a.until != null && a.until!.isBefore(now) && !(expanded && identical(a, primary));
      changed |= gone;
      return gone;
    });
    // Paused music stays a minute, then goes.
    if (media != null && !media!.playing && _pausedAt != null && now.difference(_pausedAt!) > const Duration(minutes: 1)) {
      changed |= _persistent.remove('music') != null;
    }
    final hasTimer = _persistent.values.any((a) => a.countdownEnd != null || a.countUpStart != null);
    if (changed && visible.isEmpty) expanded = false;
    if (changed || hasTimer || media?.playing == true) notifyListeners();
  }

  void _alert(NotchActivity a) {
    if (quiet && !_getsThrough(a)) return;
    _alerts.removeWhere((x) => x.id == a.id);
    _alerts.add(a);
    if (_alerts.length > 6) _alerts.removeAt(0);
    notifyListeners();
  }

  /// What comes through while the notch keeps quiet.
  static bool _getsThrough(NotchActivity a) =>
      a.kind == ActivityKind.call || a.kind == ActivityKind.timer || a.kind == ActivityKind.volume || a.preview;

  // ───────────── the PC's notch ─────────────

  String? _toldSong;
  DateTime? _toldTimerEnd;
  DateTime _toldState = DateTime.fromMillisecondsSinceEpoch(0);
  String _toldStateKey = '';

  /// Battery and song for the PC's phone tab: when it changes noticeably, else every five minutes (which also keeps
  /// the phone among the PC's devices that are there).
  void _tellPcState({bool force = false}) {
    final level = batteryLevel;
    if (level == null) return;
    final song = media?.playing == true && media?.title != null ? [media!.title, media!.artist].whereType<String>().join(' · ') : '';
    final buds = bluetoothName == null ? null : [bluetoothName, if (headphones[bluetoothName] != null) '${headphones[bluetoothName]}%'].join(' ');
    final key = '${level ~/ 10}|$plugged|$song|$wifiOn|$vpnOn|$dnd|$buds';
    final now = DateTime.now();
    if (!force && key == _toldStateKey && now.difference(_toldState) < const Duration(minutes: 5)) return;
    _toldStateKey = key;
    _toldState = now;
    _tellPc('status', '$level%', detail: song.isEmpty ? null : song, on: plugged, extra: {
      'net': wifiOn ? 'wifi' : 'cell',
      if (vpnOn) 'vpn': true,
      if (dnd) 'dnd': true,
      'bt': ?buds,
    });
  }

  /// Tells the account's PCs (through the app): a notification, a call, a song or a timer (Holm.Core PhoneStatus).
  void _tellPc(String kind, String? title,
      {String? detail, int since = 0, bool on = true, Map<String, Object?>? extra, String? key}) {
    if (!settings.toPc || title == null || title.trim().isEmpty) return;
    action?.call('phoneStatus', {'kind': kind, 'title': title, 'detail': detail, 'since': since, 'on': on, 'extra': extra, 'key': key});
  }

  // ───────────── events ─────────────

  void handle(Map<String, Object?> e) {
    // The phone's connections, whether they make news or not.
    switch (e['type']) {
      case 'wifi':
        wifiOn = e['connected'] == true;
        _tellPcState();
      case 'vpn':
        vpnOn = e['active'] == true;
        _tellPcState();
      case 'bluetooth':
        bluetoothName = e['connected'] == true ? (e['name'] as String? ?? 'Bluetooth') : null;
        final btName = e['name'] as String?;
        if (e['connected'] == true && btName != null && (e['audio'] == true || looksLikeEarbuds(btName))) {
          _budsName = btName;
        } else if (e['connected'] != true && btName == _budsName) {
          _budsName = null;
          budsLeft = budsRight = budsCase = null;
        }
        _updateBuds();
        _tellPcState();
      case 'dnd':
        dnd = e['on'] == true;
        _tellPcState();
    }
    switch (e['type']) {
      case 'settings':
        setSettings(NotchSettings.fromJsonString(e['json'] as String?));
      case 'orientation':
        landscape = e['landscape'] == true;
        if (landscape) expanded = false;
        notifyListeners();
      case 'outside':
        collapse();
      case 'icon':
        icons[e['package'] as String] = e['png'] as Uint8List;
        notifyListeners();
      case 'notification':
        _notification(e);
      case 'notificationRemoved':
        final key = e['key'] as String;
        _byKey.remove(key);
        _hidden.remove('n:$key');
        final shown = _persistent['n:$key'] != null && identical(primary, _persistent['n:$key']);
        if (_persistent.remove('n:$key') != null) {
          // Stopped from the notch (or elsewhere): close it, rather than show what's left open.
          if (shown || visible.isEmpty) expanded = false;
          notifyListeners();
        }
      case 'media':
        _media(e);
      case 'battery':
        _battery(e);
      case 'dnd':
        dnd = e['on'] == true;
      case 'volume':
        _volume(e);
      case 'budsBattery':
        budsLeft = e['left'] as int?;
        budsRight = e['right'] as int?;
        budsCase = e['case'] as int?;
        _budsName ??= e['name'] as String? ?? 'Earbuds';
        _updateBuds();
        notifyListeners();
      case 'bluetoothBattery':
        final name = e['name'] as String? ?? 'Headphones';
        final level = e['level'] as int;
        final known = headphones.containsKey(name);
        headphones[name] = level;
        // Already connected when the notch started: their battery says they're there.
        if (_budsName == null && looksLikeEarbuds(name)) _budsName = name;
        _updateBuds();
        notifyListeners();
        final bt = _alerts.where((a) => a.id == 'bt').firstOrNull;
        if (bt != null && bt.on) {
          bt.level = level;
          bt.until = DateTime.now().add(const Duration(seconds: 3));
          notifyListeners();
        } else if (!known) {
          _alert(NotchActivity('bt', ActivityKind.bluetooth, show: const Duration(seconds: 3))
            ..on = true
            ..title = name
            ..level = level);
        }
      case 'privacy':
        _privacy(e);
      case 'screenshot':
        _alert(NotchActivity('shot', ActivityKind.screenshot, show: const Duration(seconds: 6))
          ..uri = e['uri'] as String?
          ..picture = e['thumb'] as Uint8List?
          ..title = 'Screenshot'
          ..text = devices.isEmpty ? 'Send it to your PC' : 'Send it to ${devices.first}'
          ..label = 'Holm');
      case 'holm.clipboard':
        _persistent.remove('holm'); // "… is sending you something" is over
        _alert(NotchActivity('clip', ActivityKind.clipboard, show: const Duration(seconds: 5))
          ..title = 'Copied from ${e['from'] ?? 'your PC'}'
          ..text = e['text'] as String?
          ..label = 'Holm');
      case 'holm.devices':
        devices = [for (final d in (e['devices'] as List? ?? const [])) '$d'];
        notifyListeners();
      case 'wifi' when e['alert'] == true:
        final on = e['connected'] == true;
        _alert(NotchActivity('wifi', ActivityKind.wifi, show: const Duration(seconds: 3))
          ..on = on
          ..title = on ? 'Wi-Fi connected' : 'Wi-Fi disconnected');
      case 'bluetooth':
        final on = e['connected'] == true;
        final name = e['name'] as String?;
        _alert(NotchActivity('bt', ActivityKind.bluetooth, show: const Duration(seconds: 3))
          ..on = on
          ..title = name ?? 'Bluetooth'
          ..level = on && name != null ? headphones[name] : null
          ..text = on ? 'Connected' : 'Disconnected');
      case 'vpn' when e['alert'] == true:
        final on = e['active'] == true;
        final mullvad = e['mullvad'] == true;
        _alert(NotchActivity('vpn', ActivityKind.vpn, show: Duration(milliseconds: mullvad ? 4200 : 3000))
          ..on = on
          ..mullvad = mullvad
          ..title = mullvad ? 'Mullvad VPN' : 'VPN'
          ..text = on ? (mullvad ? 'Secured' : 'Connected') : 'Disconnected');
      case 'holm.transfer':
        _holmTransfer(e);
      case 'holm.chat':
        _alert(NotchActivity('chat:${e['name']}', ActivityKind.chat, show: const Duration(seconds: 5))
          ..title = e['name'] as String?
          ..text = e['text'] as String?
          ..label = 'Holm'
          ..page = 'chat');
      case 'holm.offer':
        final id = e['id'] as String;
        _persistent['offer:$id'] = NotchActivity('offer:$id', ActivityKind.holm, persistent: true, priority: 100)
          ..offerId = id
          ..title = e['sender'] as String?
          ..text = e['summary'] as String?
          ..label = e['text'] == true ? 'Text' : 'Files';
        _focus = 'offer:$id';
        expanded = true;
        notifyListeners();
      case 'holm.offerGone':
        _persistent.remove('offer:${e['id']}');
        if (visible.isEmpty) expanded = false;
        notifyListeners();
      case 'preview':
        _preview(e['on'] == true);
        expanded = e['on'] == true && e['open'] == true;
        notifyListeners();
    }
  }

  void answerOffer(NotchActivity a, bool accept) {
    action?.call('offerAnswer', {'id': a.offerId, 'accept': accept});
    _persistent.remove(a.id);
    expanded = false;
    notifyListeners();
  }

  static const _clocks = {
    'com.google.android.deskclock',
    'com.android.deskclock',
    'com.sec.android.app.clockpackage',
    'com.nothing.clock',
    'com.oneplus.deskclock',
    'com.coloros.alarmclock',
    'com.miui.clock',
  };

  void _notification(Map<String, Object?> e) {
    final key = e['key'] as String;
    final pkg = e['package'] as String;
    final initial = e['initial'] == true;
    if (e['summary'] == true || e['media'] == true) return;
    if (pkg == 'net.mullvad.mullvadvpn') return; // shown as the VPN activity
    if (settings.muted.containsKey(pkg) || snoozed(pkg)) return;
    // Empty when Android hides a notification's content from listeners (Android 15: "sensitive").
    final title = (e['title'] as String?)?.trim().isEmpty ?? true ? null : e['title'] as String?;
    final text = e['text'] as String?;
    final category = e['category'] as String?;
    final ongoing = e['ongoing'] == true;
    final max = (e['progressMax'] as int?) ?? 0, progress = (e['progress'] as int?) ?? 0;
    final indeterminate = e['indeterminate'] == true;
    final hasProgress = max > 0 || indeterminate;
    final app = e['app'] as String? ?? pkg;
    final words = '${title ?? ''} ${text ?? ''} ${e['channel'] ?? ''}'.toLowerCase();

    NotchActivity persistent(ActivityKind kind, int priority) {
      final a = NotchActivity('n:$key', kind, persistent: true, priority: priority)
        ..package = pkg
        ..notificationKey = key
        ..label = app
        ..title = title ?? app
        ..text = text
        ..progress = max > 0 ? (progress / max).clamp(0.0, 1.0) : null
        ..busy = indeterminate
        ..actions = _actions(e);
      _persistent['n:$key'] = a;
      _byKey[key] = a;
      notifyListeners();
      return a;
    }

    if (pkg == 'com.google.android.gms' && (words.contains('quick share') || words.contains('nearby'))) {
      if (ongoing || hasProgress) {
        persistent(ActivityKind.quickShare, 65).label = 'Quick Share';
        notifyListeners();
      } else if (!initial) {
        _alert(NotchActivity('qs:$key', ActivityKind.quickShare, show: const Duration(seconds: 4))
          ..package = pkg
          ..notificationKey = key
          ..label = 'Quick Share'
          ..title = title ?? 'Quick Share'
          ..text = text
          ..done = true);
      }
      return;
    }
    if (e['promoted'] == true) {
      persistent(ActivityKind.liveUpdate, 70).text = (e['shortText'] as String?) ?? text;
      notifyListeners();
      return;
    }
    final callType = (e['callType'] as int?) ?? 0;
    if (callType != 0 || (category == 'call' && ongoing)) {
      final ringing = callType == 1 || (callType == 0 && !ongoing);
      if (callType == 3) return; // the phone app screening a call by itself
      final a = persistent(ActivityKind.call, ringing ? 100 : 88)
        // Android 15 hands listeners a reduced call notification, titled with the phone app's name.
        ..title = (e['caller'] as String?) ?? (title == null || title == app ? 'Call' : title)
        ..picture = e['picture'] as Uint8List?
        ..callType = ringing ? 1 : 2
        ..text = ringing ? 'Incoming call' : null
        ..actions = const [];
      if (!ringing && e['chronometer'] == true) a.countUpStart = DateTime.fromMillisecondsSinceEpoch((e['when'] as int?) ?? 0);
      if (ringing && !initial) {
        _focus = a.id;
        expanded = true; // Answer and Decline, right there
        _tellPc('call', a.title);
      }
      if (!ringing) callMuted = false;
      notifyListeners();
      return;
    }
    if (ongoing && (category == 'navigation' || pkg == 'com.google.android.apps.maps' || pkg == 'com.waze')) {
      persistent(ActivityKind.navigation, 72).picture = e['picture'] as Uint8List?;
      notifyListeners();
      return;
    }
    final chrono = e['chronometer'] == true;
    final clock = _clocks.contains(pkg);
    final channel = '${e['channel'] ?? ''}'.toLowerCase();
    final clockTimer = clock &&
        (channel.contains('timer') || channel.contains('stopwatch') || category == 'stopwatch' || parseClockSortKey(e['sortKey'] as String?) != null);
    if (clockTimer || (ongoing && (chrono || (clock && (category == 'alarm' || category == 'progress'))))) {
      final clockState = parseClockSortKey(e['sortKey'] as String?);
      final up = clockState != null && !clockState.running && (clockState.state == 'EXPIRED' || clockState.state == 'MISSED');
      // Time's up first, then running timers, then paused ones.
      final wasUp = _persistent['n:$key']?.done ?? false;
      // Clock keeps one notification for every timer that went off: a new one changes its sort key.
      final sortKey = e['sortKey'] as String?;
      final another = _sortKeys[key] != null && _sortKeys[key] != sortKey;
      _sortKeys[key] = sortKey;
      final a = persistent(ActivityKind.timer, up ? 85 : (clockState == null || clockState.running ? 80 : 75));
      if (up && (!wasUp || another) && !initial) {
        // Time's up: open the notch, so Stop is right there (Android's own pop-up may be hidden).
        _focus = a.id;
        expanded = true;
      }
      a.title = title ?? (channel.contains('stopwatch') ? 'Stopwatch' : 'Timer');
      a.recording = isScreenRecording(pkg, channel, title);
      final when = DateTime.fromMillisecondsSinceEpoch((e['when'] as int?) ?? 0);
      final posted = DateTime.fromMillisecondsSinceEpoch((e['posted'] as int?) ?? DateTime.now().millisecondsSinceEpoch);
      if (up && (!wasUp || another) && !initial) _tellPc('timer', a.title == "Time's up" ? 'Timer' : a.title);
      if (clockState != null) {
        a.on = clockState.running;
        if (clockState.running) {
          a.countdownEnd = posted.add(clockState.left);
          final end = a.countdownEnd!;
          if (!initial && (_toldTimerEnd == null || end.difference(_toldTimerEnd!).abs() > const Duration(seconds: 5))) {
            _toldTimerEnd = end;
            _tellPc('timer', a.title, since: end.millisecondsSinceEpoch ~/ 1000);
          }
        } else if (clockState.state == 'EXPIRED' || clockState.state == 'MISSED') {
          a.done = true;
          a.title = "Time's up";
          a.text = null;
        } else {
          a.text = _fmt(clockState.left); // paused: the time left, still
        }
      } else if (chrono && e['countDown'] == true) {
        a.countdownEnd = when;
      } else if (chrono) {
        a.countUpStart = when;
      }
      notifyListeners(); // again: the shape depends on what was just filled in (time's up widens the notch)
      return;
    }
    if (ongoing && hasProgress) {
      persistent(ActivityKind.download, 60);
      return;
    }
    // A download that was running and now isn't: it's done.
    final before = _byKey[key];
    if (before != null && before.kind == ActivityKind.download && !hasProgress) {
      _persistent.remove('n:$key');
      _byKey.remove(key);
      _alert(NotchActivity('done:$key', ActivityKind.download, show: const Duration(seconds: 4))
        ..package = pkg
        ..notificationKey = key
        ..label = app
        ..title = title ?? before.title
        ..text = text ?? 'Downloaded'
        ..done = true
        ..progress = 1);
      return;
    }
    // The same notification, no longer ongoing (a screen recording now "saved", a call ended): what it showed goes.
    if (!ongoing && _persistent.remove('n:$key') != null) {
      _byKey.remove(key);
      notifyListeners();
    }
    if (ongoing || initial) return;
    if (((e['importance'] as int?) ?? 3) < 3) return; // silent ones stay in the shade
    if (!settings.systemNotices && isSystemNotice(pkg, category)) return; // battery saver and the like
    final reminder = category == 'reminder' || category == 'event' || category == 'alarm';
    final kind = reminder ? ActivityKind.reminder : ActivityKind.notification;
    final a = NotchActivity('n:$key', kind, show: const Duration(seconds: 5))
      ..package = pkg
      ..notificationKey = key
      ..label = Brand.of(pkg)?.name ?? app
      ..title = title ?? app
      ..text = text
      ..actions = _actions(e)
      ..replyIndex = _replyIndex(e);
    // An alarm going off comes through when the notch keeps quiet; a message being typed to isn't replaced.
    if (quiet && category == 'alarm') {
      _alerts.removeWhere((x) => x.id == a.id);
      _alerts.add(a);
      notifyListeners();
      return;
    }
    if (typing && _alerts.any((x) => x.id == a.id)) return;
    _alert(a);
    if (!settings.muted.containsKey(pkg)) {
      _tellPc('notification', a.title, detail: text == null ? a.label : '${a.label} · $text', key: key);
    }
  }

  static String _fmt(Duration d) {
    final s = d.inSeconds.clamp(0, 1 << 30);
    String two(int n) => n.toString().padLeft(2, '0');
    return s >= 3600 ? '${s ~/ 3600}:${two(s % 3600 ~/ 60)}:${two(s % 60)}' : '${s ~/ 60}:${two(s % 60)}';
  }

  static List<(int, String)> _actions(Map<String, Object?> e) => [
        for (final a in (e['actions'] as List? ?? const []))
          if (a is List && a.length >= 2 && a[0] is int && (a[1] as String? ?? '').isNotEmpty && !(a.length > 2 && a[2] == true))
            (a[0] as int, a[1] as String),
      ].take(3).toList();

  static int? _replyIndex(Map<String, Object?> e) {
    for (final a in (e['actions'] as List? ?? const [])) {
      if (a is List && a.length > 2 && a[2] == true && a[0] is int) return a[0] as int;
    }
    return null;
  }

  /// Android's screen recorder (System UI, or the Screen recorder app) while it records.
  static bool isScreenRecording(String pkg, String channel, String? title) {
    final t = (title ?? '').toLowerCase();
    return (pkg == 'com.android.systemui' || pkg.contains('screenrecord')) &&
        (channel.contains('screen_record') || channel.contains('screenrecord') || t.contains('record') || t.contains('aufnahme') || t.contains('aufzeichn'));
  }

  /// Android's own notices: battery saver, USB, "running in the background", system updates.
  static bool isSystemNotice(String pkg, String? category) =>
      const {'android', 'com.android.systemui', 'com.android.settings', 'com.android.shell', 'com.google.android.settings.intelligence'}
          .contains(pkg) ||
      category == 'sys' ||
      category == 'status';

  /// Presses one of the notification's buttons. A message's ("Mark as read") closes the notch; something ongoing
  /// ("Add 1 min" on a timer) stays open, and closes once its notification goes (Stop).
  void pressAction(NotchActivity a, int index) {
    if (a.notificationKey == null) return;
    action?.call('notificationAction', {'key': a.notificationKey, 'index': index});
    if (a.isAlert) {
      _alerts.remove(a);
      expanded = false;
    }
    notifyListeners();
  }

  void _media(Map<String, Object?> e) {
    if (e['active'] != true) {
      media = null;
      _persistent.remove('music');
      _hidden.remove('music');
      notifyListeners();
      return;
    }
    final m = MediaInfo(
      package: e['package'] as String,
      app: e['app'] as String? ?? '',
      title: e['title'] as String?,
      artist: e['artist'] as String?,
      playing: e['playing'] == true,
      position: (e['position'] as int?) ?? 0,
      duration: (e['duration'] as int?) ?? 0,
      updated: (e['updated'] as int?) ?? 0,
      speed: (e['speed'] as num?)?.toDouble() ?? 1,
      canSeek: e['canSeek'] == true,
    );
    // Swiped away, the music stays hidden only for that song: the next one, or playing again after a pause, shows it.
    final before = media;
    if (_hidden.contains('music') &&
        before != null &&
        ((m.playing && !before.playing) || m.title != before.title || m.package != before.package)) {
      _hidden.remove('music');
    }
    media = m;
    mediaReceived = DateTime.now();
    final art = e['art'] as Uint8List?;
    final artKey = e['artKey'] as String?;
    // A new song, or the same song's cover arriving late (players send the title first, the picture after).
    if (artKey != _artKey || (this.art == null && art != null)) {
      _artKey = artKey;
      this.art = art;
      cover = null;
      if (art != null) {
        CoverColors.of(art).then((c) {
          if (_artKey == artKey) {
            cover = c;
            notifyListeners();
          }
        });
      }
    }
    _pausedAt = m.playing ? null : (_pausedAt ?? DateTime.now());
    final songBefore = _toldSong;
    if (m.playing && m.title != null && m.title != _toldSong) {
      _toldSong = m.title;
      _tellPc('music', m.title, detail: m.artist);
    } else if (!m.playing && _toldSong != null) {
      _toldSong = null;
      _tellPc('music', m.title ?? 'Music', on: false);
    }
    if (_toldSong != songBefore) _tellPcState(force: true);
    if (m.playing || _persistent.containsKey('music')) {
      _persistent['music'] = (_persistent['music'] ?? NotchActivity('music', ActivityKind.music, persistent: true, priority: 50))
        ..package = m.package
        ..title = m.title
        ..text = m.artist
        ..label = m.app;
    }
    notifyListeners();
  }

  void _volume(Map<String, Object?> e) {
    final level = (e['level'] as num?)?.toDouble() ?? 0;
    final a = _alerts.where((a) => a.id == 'volume').firstOrNull;
    if (a != null && a.until != null && a.until!.isAfter(DateTime.now())) {
      a.progress = level;
      a.on = level > 0;
      a.until = DateTime.now().add(const Duration(milliseconds: 1600));
      notifyListeners();
      return;
    }
    _alert(NotchActivity('volume', ActivityKind.volume, show: const Duration(milliseconds: 1600))
      ..progress = level
      ..on = level > 0
      ..title = e['call'] == true ? 'Call' : 'Volume');
  }

  void _privacy(Map<String, Object?> e) {
    final camera = e['camera'] == true, mic = e['mic'] == true;
    if (!camera && !mic) {
      if (_persistent.remove('privacy') != null) notifyListeners();
      return;
    }
    _persistent['privacy'] = NotchActivity('privacy', ActivityKind.privacy, persistent: true, priority: 15)
      ..on = camera
      ..package = e['package'] as String?
      ..label = e['app'] as String?
      ..title = camera && mic ? 'Camera and microphone' : camera ? 'Camera' : 'Microphone'
      ..text = e['app'] as String?;
    notifyListeners();
  }

  /// Sends the screenshot to the PC (the app does, with the last device it sent to).
  void beamScreenshot(NotchActivity a) {
    if (a.uri == null) return;
    action?.call('beamScreenshot', a.uri);
    _alerts.remove(a);
    expanded = false;
    notifyListeners();
  }

  void _battery(Map<String, Object?> e) {
    batteryLevel = e['level'] as int?;
    final wasPlugged = plugged, known = _batteryKnown, wasFull = _full;
    plugged = e['plugged'] == true;
    _full = plugged && (e['full'] == true || (batteryLevel ?? 0) >= 100);
    _batteryKnown = true;
    _tellPcState(force: wasPlugged != plugged);
    // Fully charged while plugged in: here, and on the PC (it says so in its notch).
    if (known && _full && !wasFull && wasPlugged == plugged) {
      _alert(NotchActivity('battery', ActivityKind.charging, show: const Duration(milliseconds: 3500))
        ..on = true
        ..level = batteryLevel
        ..title = 'Charged');
      _tellPc('battery', 'Charged', detail: '${batteryLevel ?? 100}%');
      return;
    }
    // Plugging in shows up first as a battery change, then as "power connected": the switch itself counts, from
    // whichever comes first. The first report after starting is how things were, not news.
    if (known && wasPlugged != plugged) {
      _alert(NotchActivity('battery', ActivityKind.charging, show: const Duration(milliseconds: 3500))
        ..on = plugged
        ..level = batteryLevel
        ..title = plugged ? (e['full'] == true ? 'Charged' : 'Charging') : 'Unplugged');
    } else {
      final a = _alerts.where((a) => a.id == 'battery').firstOrNull;
      if (a != null) a.level = batteryLevel;
      notifyListeners();
    }
  }

  void _holmTransfer(Map<String, Object?> e) {
    if (e['clear'] == true) {
      if (_persistent.remove('holm') != null) notifyListeners();
      return;
    }
    final done = e['done'] == true;
    final a = NotchActivity('holm', ActivityKind.holm, persistent: !done, priority: 90, show: done ? const Duration(seconds: 4) : null)
      ..title = e['title'] as String?
      ..text = e['detail'] as String?
      ..progress = (e['progress'] as num?)?.toDouble()
      ..busy = !done && e['progress'] == null
      ..done = done
      ..failed = e['failed'] == true
      ..label = 'Holm'
      ..page = 'devices';
    if (done) {
      _persistent.remove('holm');
      _alert(a);
    } else {
      _persistent['holm'] = a;
      notifyListeners();
    }
  }

  /// Sample activities while the settings page is open, so the shape can be fitted to the camera.
  void _preview(bool on) {
    _persistent.removeWhere((_, a) => a.preview);
    _alerts.removeWhere((a) => a.preview);
    if (on) {
      _persistent['preview-music'] = NotchActivity('preview-music', ActivityKind.music, persistent: true, priority: 49)
        ..preview = true
        ..title = media?.title ?? 'Preview'
        ..text = media?.artist ?? 'Holm'
        ..label = 'Music';
      // The earbuds beside it, as they look in your colour (the setting on that page).
      _persistent['preview-buds'] = NotchActivity('preview-buds', ActivityKind.buds, persistent: true, priority: -50)
        ..preview = true
        ..title = 'Pixel Buds Pro 2'
        ..level = 80;
      _persistent['preview-timer'] = NotchActivity('preview-timer', ActivityKind.timer, persistent: true, priority: 48)
        ..preview = true
        ..title = 'Timer'
        ..countdownEnd = DateTime.now().add(const Duration(minutes: 4, seconds: 59));
    }
    notifyListeners();
  }

  Color accentFor(NotchActivity a) {
    final own = settings.colors[a.package];
    if (own != null) return Color(own);
    if (a.kind == ActivityKind.music && cover != null) return cover!.accent;
    final brand = Brand.of(a.package);
    if (brand != null) return brand.color;
    if (a.recording) return const Color(0xFFFF453A);
    if (a.kind == ActivityKind.buds) {
      final l = a.level ?? 100;
      return l <= 15 ? const Color(0xFFFF453A) : l <= 30 ? const Color(0xFFFF9F0A) : const Color(0xFF30D158);
    }
    return switch (a.kind) {
      ActivityKind.timer => const Color(0xFFFF9F0A),
      ActivityKind.charging => const Color(0xFF30D158),
      ActivityKind.download || ActivityKind.quickShare => const Color(0xFF0A84FF),
      ActivityKind.vpn => a.on ? const Color(0xFF30D158) : const Color(0xFFFF453A),
      ActivityKind.wifi || ActivityKind.bluetooth => a.on ? const Color(0xFF0A84FF) : const Color(0xFF8E8E93),
      ActivityKind.holm || ActivityKind.chat => const Color(0xFF7B4DFF),
      ActivityKind.reminder => const Color(0xFFFFC43D),
      ActivityKind.call => const Color(0xFF30D158),
      ActivityKind.navigation => const Color(0xFF34A853),
      ActivityKind.privacy => a.on ? const Color(0xFF30D158) : const Color(0xFFFF9F0A),
      ActivityKind.clipboard || ActivityKind.screenshot => const Color(0xFF7B4DFF),
      ActivityKind.volume => const Color(0xFFFFFFFF),
      _ => const Color(0xFFEBEBF0),
    };
  }
}

/// Google's Clock writes a timer's state into the notification's sort key, e.g. "0|↺2|RUNNING|▶09:31:40.624|Σ0:05:00|Δ0:01:29|⏳0:03:31".
({String state, bool running, Duration left})? parseClockSortKey(String? key) {
  if (key == null) return null;
  final left = RegExp(r'⏳(\d+):(\d{2}):(\d{2})').firstMatch(key);
  final found = RegExp(r'\|(RUNNING|PAUSED|EXPIRED|RESET|MISSED)(\||$)').firstMatch(key)?.group(1);
  if (left == null && found == null) return null;
  final state = found ?? 'RUNNING';
  // A timer that went off ("…|EXPIRED|", in the "Firing" notification) has no time left.
  final d = left == null
      ? Duration.zero
      : Duration(hours: int.parse(left.group(1)!), minutes: int.parse(left.group(2)!), seconds: int.parse(left.group(3)!));
  return (state: state, running: state == 'RUNNING', left: d);
}
