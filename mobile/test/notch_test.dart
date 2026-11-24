// The Dynamic Notch's logic: which events become which activities, what shows first, settings and springs.
import 'package:flutter_test/flutter_test.dart';
import 'package:holm_mobile/notch/activities.dart';
import 'package:holm_mobile/notch/settings.dart';
import 'package:holm_mobile/notch/spring.dart';
import 'package:holm_mobile/update.dart';

Map<String, Object?> notification(String key, {String pkg = 'com.whatsapp', bool ongoing = false, int progress = 0, int max = 0,
        bool chrono = false, bool countDown = false, String? category, bool initial = false, bool promoted = false}) =>
    {
      'type': 'notification',
      'key': key,
      'package': pkg,
      'app': 'App',
      'title': 'Title $key',
      'text': 'Text',
      'ongoing': ongoing,
      'progress': progress,
      'progressMax': max,
      'chronometer': chrono,
      'countDown': countDown,
      'when': DateTime.now().add(const Duration(minutes: 5)).millisecondsSinceEpoch,
      'category': category,
      'initial': initial,
      'promoted': promoted,
      'importance': 3,
    };

void main() {
  test('notifications become the right activities', () {
    final c = NotchController();
    c.handle(notification('a'));
    expect(c.primary!.kind, ActivityKind.notification);
    expect(c.primary!.label, 'WhatsApp'); // styled as the known app

    c.handle(notification('d', pkg: 'com.android.chrome', ongoing: true, progress: 30, max: 100));
    expect(c.visible.map((a) => a.kind), contains(ActivityKind.download));
    c.handle(notification('t', pkg: 'com.google.android.deskclock', ongoing: true, chrono: true, countDown: true));
    final timer = c.visible.firstWhere((a) => a.kind == ActivityKind.timer, orElse: () => c.visible.last);
    expect(timer.kind, ActivityKind.timer);
    expect(timer.countdownEnd, isNotNull);
    c.dispose();
  });

  test('a download that stops showing progress is done', () {
    final c = NotchController()..setSettings(const NotchSettings(maxActivities: 1));
    c.handle(notification('d', pkg: 'com.android.chrome', ongoing: true, progress: 90, max: 100));
    expect(c.primary!.kind, ActivityKind.download);
    c.handle(notification('d', pkg: 'com.android.chrome'));
    expect(c.primary!.done, isTrue);
    c.dispose();
  });

  test('what was there at the start does not pop out, and switched-off kinds stay hidden', () {
    final c = NotchController();
    c.handle(notification('old', initial: true));
    expect(c.primary, isNull);
    c.setSettings(const NotchSettings(off: {ActivityKind.notification}));
    c.handle(notification('new'));
    expect(c.primary, isNull);
    c.dispose();
  });

  test('Holm offers go first and open the notch', () {
    final c = NotchController();
    c.handle(notification('a'));
    c.handle({'type': 'holm.offer', 'id': '1', 'sender': 'Office PC', 'summary': 'photo.jpg', 'text': false});
    expect(c.expanded, isTrue);
    expect(c.visible.any((a) => a.isOffer), isTrue);
    String? answered;
    c.action = (method, args) async => answered = '$method ${(args as Map)['accept']}';
    c.answerOffer(c.visible.firstWhere((a) => a.isOffer), true);
    expect(answered, 'offerAnswer true');
    c.dispose();
  });

  test('landscape hides everything', () {
    final c = NotchController();
    c.handle(notification('a'));
    c.handle({'type': 'orientation', 'landscape': true});
    expect(c.visible, isEmpty);
    c.dispose();
  });

  test('settings survive JSON', () {
    const s = NotchSettings(maxActivities: 1, look: NotchLook.nothing, off: {ActivityKind.vpn},
        closed: NotchGeometry(width: 100, height: 30, radius: 15, x: -4, y: 12));
    final back = NotchSettings.fromJsonString(s.toJsonString());
    expect(back.maxActivities, 1);
    expect(back.look, NotchLook.nothing);
    expect(back.off, {ActivityKind.vpn});
    expect(back.closed.x, -4);
    expect(NotchSettings.fromJsonString('garbage').maxActivities, 2);
  });

  test('springs overshoot a little and settle on the target', () {
    final s = Spring(0)..retarget(100, 0.34, 0.63);
    var peak = 0.0;
    for (var i = 0; i < 240 && !s.settled; i++) {
      s.step(1 / 60);
      if (s.value > peak) peak = s.value;
    }
    expect(s.settled, isTrue);
    expect(s.value, 100);
    expect(peak, greaterThan(100)); // the desktop's slight bounce
  });

  test("Google Clock's timer is read from its sort key", () {
    final k = parseClockSortKey('0|↺2|RUNNING|▶09:31:40.624|Σ0:05:00|Δ0:01:29|⏳0:03:31')!;
    expect(k.running, isTrue);
    expect(k.left, const Duration(minutes: 3, seconds: 31));
    expect(parseClockSortKey('0|↺2|PAUSED|Σ0:05:00|⏳1:00:05')!.running, isFalse);
    expect(parseClockSortKey('nothing'), isNull);
    final up = parseClockSortKey('0 2|↺2|EXPIRED|')!;
    expect(up.state, 'EXPIRED');
    expect(up.left, Duration.zero);

    final c = NotchController();
    c.handle({
      ...notification('clock', pkg: 'com.google.android.deskclock'),
      'title': null,
      'channel': 'Timers',
      'sortKey': '0|↺2|RUNNING|▶09:31:40.624|Σ0:05:00|Δ0:01:29|⏳0:03:31',
      'posted': DateTime.now().millisecondsSinceEpoch,
    });
    expect(c.primary!.kind, ActivityKind.timer);
    expect(c.primary!.countdownEnd!.difference(DateTime.now()).inSeconds, inInclusiveRange(205, 211));
    c.dispose();
  });

  test("time's up widens the notch as soon as it is known", () {
    final c = NotchController();
    var wideWhenTold = false;
    c.addListener(() => wideWhenTold = c.primary?.wide ?? false);
    c.handle({
      ...notification('fire', pkg: 'com.google.android.deskclock', ongoing: true, category: 'alarm'),
      'title': null,
      'channel': 'Firing',
      'sortKey': '0 4|↺2|EXPIRED|▶10:03:05.600|Σ0:00:05|Δ0:00:05|⏳0:00:00',
    });
    expect(c.primary!.done, isTrue);
    expect(wideWhenTold, isTrue);
    c.dispose();
  });

  test('plugging in shows charging, whichever report comes first', () {
    final c = NotchController();
    c.handle({'type': 'battery', 'level': 50, 'plugged': false, 'alert': false}); // how it was at the start
    expect(c.primary, isNull);
    c.handle({'type': 'battery', 'level': 50, 'plugged': true, 'alert': false}); // the battery change comes first
    expect(c.primary?.kind, ActivityKind.charging);
    expect(c.primary?.title, 'Charging');
    c.dispose();
  });

  test('each open corner keeps its own radius through the settings', () {
    final s = const NotchSettings().copyWith(
      open: const NotchSettings().open.copyWith(corners: [10, 20, 30, 40]),
      muted: {'com.example': 'Example'},
      systemNotices: true,
    );
    final back = NotchSettings.fromJsonString(s.toJsonString());
    expect([for (var i = 0; i < 4; i++) back.open.corner(i)], [10, 20, 30, 40]);
    expect(back.closed.corners, isNull);
    expect(back.closed.corner(2), back.closed.radius);
    expect(back.muted, {'com.example': 'Example'});
    expect(back.systemNotices, isTrue);
    expect(back.open.copyWith(corners: null).corners, isNull);
  });

  test('battery saver and other system notices stay away unless asked for; muted apps too', () {
    final c = NotchController();
    c.handle(notification('saver', pkg: 'com.android.systemui'));
    expect(c.primary, isNull);
    c.setSettings(const NotchSettings(systemNotices: true));
    c.handle(notification('saver2', pkg: 'com.android.systemui'));
    expect(c.primary, isNotNull);
    c.swipeAway(c.primary!);
    expect(c.primary, isNull);

    c.handle(notification('a'));
    final a = c.primary!;
    expect(c.canMute(a), isTrue);
    c.mute(a);
    expect(c.settings.muted.keys, contains('com.whatsapp'));
    c.handle(notification('b'));
    expect(c.primary, isNull);
    c.dispose();
  });

  test('a timer button keeps the notch open; a message button closes it', () {
    final c = NotchController();
    c.handle(notification('t', pkg: 'com.google.android.deskclock', ongoing: true, chrono: true, countDown: true));
    c.expand();
    c.pressAction(c.primary!, 1); // Add 1 min
    expect(c.expanded, isTrue);
    c.handle({'type': 'notificationRemoved', 'key': 't'}); // Stop
    expect(c.expanded, isFalse);

    c.handle(notification('m'));
    c.expand();
    c.pressAction(c.primary!, 0);
    expect(c.expanded, isFalse);
    c.dispose();
  });

  test('latest.json only names a file on the update server, with its checksum', () {
    final sha = 'a' * 64;
    expect(UpdateInfo.fromJson({'versionCode': 3, 'versionName': '1.2.0', 'file': 'holm-1.2.0-3.apk', 'sha256': sha})?.code, 3);
    expect(UpdateInfo.fromJson({'versionCode': 3, 'versionName': '1.2.0', 'file': '../x.apk', 'sha256': sha}), isNull);
    expect(UpdateInfo.fromJson({'versionCode': 3, 'versionName': '1.2.0', 'file': 'https://evil/x.apk', 'sha256': sha}), isNull);
    expect(UpdateInfo.fromJson({'versionCode': 3, 'versionName': '1.2.0', 'file': 'h.apk', 'sha256': 'nope'}), isNull);
  });

  test('a call rings in the open notch, then counts its time; the PC hears of it', () {
    final c = NotchController();
    final told = <String>[];
    c.action = (method, args) async {
      if (method == 'phoneStatus') told.add('${(args as Map)['kind']}:${args['title']}');
      return null;
    };
    c.handle({...notification('call', pkg: 'com.google.android.dialer', ongoing: true), 'title': 'Lena', 'callType': 1});
    expect(c.primary!.kind, ActivityKind.call);
    expect(c.expanded, isTrue);
    expect(told, ['call:Lena']);
    c.handle({...notification('call', pkg: 'com.google.android.dialer', ongoing: true, chrono: true), 'title': 'Lena', 'callType': 2});
    expect(c.primary!.callType, 2);
    expect(c.primary!.countUpStart, isNotNull);
    c.dispose();
  });

  test('keeping quiet lets only calls, timers and alarms through', () {
    final c = NotchController()..setSettings(const NotchSettings(quiet: QuietMode.dnd));
    c.handle({'type': 'dnd', 'on': true});
    c.handle(notification('m'));
    expect(c.primary, isNull);
    c.handle(notification('alarm', pkg: 'com.google.android.deskclock', category: 'alarm'));
    expect(c.primary, isNotNull);
    c.handle({'type': 'dnd', 'on': false});
    c.handle(notification('m2'));
    expect(c.primary!.notificationKey, 'm2');
    expect(const NotchSettings(quietFrom: 22 * 60, quietTo: 7 * 60).inQuietHours(DateTime(2026, 1, 1, 23)), isTrue);
    expect(const NotchSettings(quietFrom: 22 * 60, quietTo: 7 * 60).inQuietHours(DateTime(2026, 1, 1, 12)), isFalse);
    c.dispose();
  });

  test('a message answers in the notch; a swipe to the right clears it from the shade too', () async {
    final c = NotchController();
    final calls = <String>[];
    c.action = (method, args) async {
      calls.add(method);
      return method == 'reply';
    };
    c.handle({...notification('wa'), 'actions': [[0, 'Reply', true], [1, 'Mark as read', false]]});
    final a = c.primary!;
    expect(a.replyIndex, 0);
    expect(a.actions.map((x) => x.$2), ['Mark as read']);
    c.expand();
    c.startTyping(a);
    expect(c.typing, isTrue);
    expect(await c.reply(a, 'On my way'), isTrue);
    expect(c.typing, isFalse);
    expect(c.expanded, isFalse);
    expect(calls, containsAllInOrder(['keyboard', 'reply', 'keyboard']));

    c.handle(notification('other'));
    c.swipeAway(c.primary!, clear: true);
    expect(calls.last, 'clear');
    expect(c.primary, isNull);
    c.dispose();
  });

  test('the volume updates in place; the overview opens with a tap on the camera', () {
    final c = NotchController();
    c.handle({'type': 'volume', 'level': 0.4});
    c.handle({'type': 'volume', 'level': 0.5});
    expect(c.visible.where((a) => a.kind == ActivityKind.volume).length, 1);
    expect(c.primary!.progress, 0.5);
    expect(c.primary!.wide, isTrue);

    final idle = NotchController();
    idle.showHome();
    expect(idle.expanded, isTrue);
    expect(idle.primary!.kind, ActivityKind.home);
    idle.collapse();
    expect(idle.primary, isNull);
    c.dispose();
    idle.dispose();
  });

  test('settings keep the new choices', () {
    final s = const NotchSettings().copyWith(
      design: 'paper',
      colors: {'com.whatsapp': 0xFF30D158},
      quiet: QuietMode.hours,
      quietFrom: 60,
      doubleTap: NotchGesture.playPause,
      betaUpdates: true,
      toPc: false,
    );
    final back = NotchSettings.fromJsonString(s.toJsonString());
    expect(back.design, 'paper');
    expect(back.colors['com.whatsapp'], 0xFF30D158);
    expect(back.quiet, QuietMode.hours);
    expect(back.quietFrom, 60);
    expect(back.doubleTap, NotchGesture.playPause);
    expect(back.betaUpdates, isTrue);
    expect(back.toPc, isFalse);
    expect(back.copyWith(design: null).design, isNull);
  });

  test('fully charged: the notch says so, and tells the PC', () {
    final c = NotchController();
    final told = <String>[];
    c.action = (method, args) async {
      if (method == 'phoneStatus') told.add('${(args as Map)['kind']}:${args['detail']}');
      return null;
    };
    c.handle({'type': 'battery', 'level': 97, 'plugged': true, 'full': false});
    c.handle({'type': 'battery', 'level': 99, 'plugged': true, 'full': false});
    expect(told, isEmpty);
    c.handle({'type': 'battery', 'level': 100, 'plugged': true, 'full': true});
    expect(c.primary!.title, 'Charged');
    expect(told, ['battery:100%']);
    c.handle({'type': 'battery', 'level': 100, 'plugged': true, 'full': true});
    expect(told.length, 1); // once
    c.dispose();
  });
}
