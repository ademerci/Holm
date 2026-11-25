import 'dart:convert';

/// The notch's shape in one state: size, corner radius and where it sits (dp; x from the screen's centre, y from its top).
/// Open, each corner may have its own radius ([corners]: top left, top right, bottom right, bottom left).
class NotchGeometry {
  final double width, height, radius, x, y;
  final List<double>? corners;
  const NotchGeometry({required this.width, required this.height, required this.radius, this.x = 0, required this.y, this.corners});

  static const _keep = Object();

  NotchGeometry copyWith({double? width, double? height, double? radius, double? x, double? y, Object? corners = _keep}) =>
      NotchGeometry(
          width: width ?? this.width,
          height: height ?? this.height,
          radius: radius ?? this.radius,
          x: x ?? this.x,
          y: y ?? this.y,
          corners: identical(corners, _keep)
              ? this.corners
              : (corners as List?)?.map((e) => (e as num).toDouble()).toList());

  /// The radius of corner [i] (0 top left, 1 top right, 2 bottom right, 3 bottom left).
  double corner(int i) => corners?[i] ?? radius;

  Map<String, dynamic> toJson() => {'w': width, 'h': height, 'r': radius, 'x': x, 'y': y, if (corners != null) 'c': corners};
  static NotchGeometry fromJson(Object? j, NotchGeometry fallback) {
    if (j is! Map) return fallback;
    double v(String k, double d) => (j[k] as num?)?.toDouble() ?? d;
    final c = j['c'];
    return NotchGeometry(
        width: v('w', fallback.width),
        height: v('h', fallback.height),
        radius: v('r', fallback.radius),
        x: v('x', fallback.x),
        y: v('y', fallback.y),
        corners: c is List && c.length == 4 ? [for (final e in c) (e as num).toDouble()] : null);
  }
}

/// What can show in the notch; each can be switched off.
enum ActivityKind {
  music('Music', 'Spotify and any player, with controls'),
  notification('Notifications', 'New notifications, styled for well-known apps, with a reply right there'),
  call('Calls', 'Answer and decline, and the call’s time while you talk'),
  timer('Timers', 'Running timers and stopwatches'),
  navigation('Navigation', 'The next turn from Google Maps'),
  download('Downloads', 'Progress until the download is done'),
  holm('Holm', 'Files and texts from your PC, offers to accept'),
  chat('Holm chat', 'New messages from friends'),
  clipboard('Clipboard from your PC', 'Text copied on your PC, to copy here with a tap'),
  screenshot('Screenshots to your PC', 'Send a screenshot you just took to your PC'),
  volume('Volume', 'The notch shows the volume instead of Android’s slider'),
  charging('Charging', 'Plugged in and unplugged, with the battery level'),
  wifi('Wi-Fi', 'Connected and disconnected'),
  bluetooth('Bluetooth', 'Headphones connecting, with their battery'),
  buds('Earbuds', 'While connected: your earbuds in 3D with their battery, small beside everything else'),
  vpn('VPN', 'On and off, with Mullvad’s mole'),
  torch('Flashlight', 'On and off, for a moment'),
  privacy('Camera and microphone', 'A dot while an app uses them'),
  quickShare('Quick Share', 'Files coming and going'),
  liveUpdate('Live Updates', 'Rides, deliveries and more (Android 16)'),
  reminder('Reminders', 'Reminders and events coming up'),
  home('Overview', 'Tap the camera when nothing shows: time, batteries, what’s next');

  final String label, detail;
  const ActivityKind(this.label, this.detail);
}

enum NotchLook { holm, nothing }

/// When the notch keeps quiet: only calls, timers and alarms come through (what's already running stays).
enum QuietMode {
  off('Never'),
  dnd('With Do Not Disturb'),
  hours('At set times');

  final String label;
  const QuietMode(this.label);
}

/// What a double tap or a long press on the closed notch does.
enum NotchGesture {
  open('Open the notch'),
  torch('Torch on and off'),
  playPause('Play and pause'),
  screenshot('Take a screenshot'),
  holm('Open Holm'),
  note('New note in Holm'),
  none('Nothing');

  final String label;
  const NotchGesture(this.label);
}

class NotchSettings {
  /// One activity at a time, or two (the second as a small bubble beside the notch).
  final int maxActivities;
  final NotchGeometry closed, open;
  /// The look of the open notch; closed it is always black, like the camera.
  final NotchLook look;
  /// One of Holm's designs for the open notch (lib/notch/designs.dart), null for Holm's own.
  final String? design;
  final Set<ActivityKind> off;
  /// Apps whose notifications never show in the notch (package → name, for the list in the settings).
  final Map<String, String> muted;
  /// Android's own notices (battery saver, USB, system updates): off unless asked for, they come often.
  final bool systemNotices;
  /// An app's own colour in the notch (package → ARGB), over the built-in brand colours.
  final Map<String, int> colors;
  final QuietMode quiet;
  /// Quiet hours, in minutes after midnight (from may be after to: over midnight).
  final int quietFrom, quietTo;
  final NotchGesture doubleTap, longPress;
  /// Test versions of Holm from the update server, before everyone gets them.
  final bool betaUpdates;
  /// The PCs signed in to the same Holm account show the phone's notifications, calls, songs and timers.
  final bool toPc;
  /// Notifications only as tall as the closed notch: the sender left of the camera, the message right of it.
  final bool compactBanners;
  /// The colour of your Pixel Buds Pro 2 for their 3D picture: porcelain, hazel, wintergreen or peony.
  final String budsColor;

  const NotchSettings({
    this.maxActivities = 2,
    this.closed = const NotchGeometry(width: 124, height: 34, radius: 17, y: 10),
    this.open = const NotchGeometry(width: 368, height: 250, radius: 38, y: 8),
    this.look = NotchLook.holm,
    this.design,
    this.off = const {},
    this.muted = const {},
    this.systemNotices = false,
    this.colors = const {},
    this.quiet = QuietMode.dnd,
    this.quietFrom = 22 * 60,
    this.quietTo = 7 * 60,
    this.doubleTap = NotchGesture.torch,
    this.longPress = NotchGesture.open,
    this.betaUpdates = false,
    this.toPc = true,
    this.compactBanners = false,
    this.budsColor = 'porcelain',
  });

  bool shows(ActivityKind kind) => !off.contains(kind);

  /// Whether [time] is in the quiet hours.
  bool inQuietHours(DateTime time) {
    final m = time.hour * 60 + time.minute;
    if (quietFrom == quietTo) return false;
    return quietFrom < quietTo ? m >= quietFrom && m < quietTo : m >= quietFrom || m < quietTo;
  }

  static const _keep = Object();

  NotchSettings copyWith({
    int? maxActivities,
    NotchGeometry? closed,
    NotchGeometry? open,
    NotchLook? look,
    Object? design = _keep,
    Set<ActivityKind>? off,
    Map<String, String>? muted,
    bool? systemNotices,
    Map<String, int>? colors,
    QuietMode? quiet,
    int? quietFrom,
    int? quietTo,
    NotchGesture? doubleTap,
    NotchGesture? longPress,
    bool? betaUpdates,
    bool? toPc,
    bool? compactBanners,
    String? budsColor,
  }) =>
      NotchSettings(
        maxActivities: maxActivities ?? this.maxActivities,
        closed: closed ?? this.closed,
        open: open ?? this.open,
        look: look ?? this.look,
        design: identical(design, _keep) ? this.design : design as String?,
        off: off ?? this.off,
        muted: muted ?? this.muted,
        systemNotices: systemNotices ?? this.systemNotices,
        colors: colors ?? this.colors,
        quiet: quiet ?? this.quiet,
        quietFrom: quietFrom ?? this.quietFrom,
        quietTo: quietTo ?? this.quietTo,
        doubleTap: doubleTap ?? this.doubleTap,
        longPress: longPress ?? this.longPress,
        betaUpdates: betaUpdates ?? this.betaUpdates,
        toPc: toPc ?? this.toPc,
        compactBanners: compactBanners ?? this.compactBanners,
        budsColor: budsColor ?? this.budsColor,
      );

  String toJsonString() => jsonEncode({
        'max': maxActivities,
        'closed': closed.toJson(),
        'open': open.toJson(),
        'look': look.name,
        'design': design,
        'off': [for (final k in off) k.name],
        'muted': muted,
        'system': systemNotices,
        'colors': colors,
        'quiet': quiet.name,
        'quietFrom': quietFrom,
        'quietTo': quietTo,
        'doubleTap': doubleTap.name,
        'longPress': longPress.name,
        'beta': betaUpdates,
        'toPc': toPc,
        'compact': compactBanners,
        'budsColor': budsColor,
      });

  static NotchSettings fromJsonString(String? text) {
    const d = NotchSettings();
    if (text == null) return d;
    try {
      final j = jsonDecode(text) as Map<String, dynamic>;
      int minutes(String k, int fallback) => ((j[k] as num?)?.toInt() ?? fallback).clamp(0, 24 * 60 - 1);
      return NotchSettings(
        maxActivities: ((j['max'] as num?)?.toInt() ?? 2).clamp(1, 2),
        closed: NotchGeometry.fromJson(j['closed'], d.closed),
        open: NotchGeometry.fromJson(j['open'], d.open),
        look: NotchLook.values.asNameMap()[j['look']] ?? NotchLook.holm,
        design: j['design'] as String?,
        off: {for (final n in (j['off'] as List? ?? [])) ?ActivityKind.values.asNameMap()[n]},
        muted: {for (final e in ((j['muted'] as Map?) ?? {}).entries) '${e.key}': '${e.value}'},
        systemNotices: j['system'] == true,
        colors: {
          for (final e in ((j['colors'] as Map?) ?? {}).entries)
            if (e.value is num) '${e.key}': (e.value as num).toInt(),
        },
        quiet: QuietMode.values.asNameMap()[j['quiet']] ?? d.quiet,
        quietFrom: minutes('quietFrom', d.quietFrom),
        quietTo: minutes('quietTo', d.quietTo),
        doubleTap: NotchGesture.values.asNameMap()[j['doubleTap']] ?? d.doubleTap,
        longPress: NotchGesture.values.asNameMap()[j['longPress']] ?? d.longPress,
        betaUpdates: j['beta'] == true,
        toPc: j['toPc'] != false,
        compactBanners: j['compact'] == true,
        budsColor: const {'porcelain', 'hazel', 'wintergreen', 'peony'}.contains(j['budsColor']) ? j['budsColor'] as String : 'porcelain',
      );
    } catch (_) {
      return d;
    }
  }
}
