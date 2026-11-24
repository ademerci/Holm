import 'dart:async';

import 'package:flutter/services.dart';

import 'settings.dart';

/// The app's side of the notch: Holm's events go to it, its settings too, and the answers to offers come back.
class NotchLink {
  static const _bridge = MethodChannel('holm/bridge');
  static const _system = MethodChannel('holm/system');
  static final _answers = <String, Completer<bool>>{};

  /// A page the notch asked the app to open ("chat", "devices").
  static void Function(String page)? onOpen;
  /// An app's notifications were switched off from the notch (package, name).
  static void Function(String package, String name)? onMute;
  /// Something on the phone for the account's PCs (kind, title, detail, since, on).
  static void Function(Map<String, Object?> status)? onPhoneStatus;
  /// Something was shared from another app; [takeShared] gets it.
  static void Function()? onShared;

  /// What another app shared with Holm (Android's share menu): text, or content:// files. Null if nothing is waiting.
  static Future<({String? text, List<String> uris})?> takeShared() async {
    final r = await _bridge.invokeMethod<Map>('takeShared').catchError((_) => null);
    if (r == null) return null;
    return (text: r['text'] as String?, uris: [for (final u in (r['uris'] as List? ?? const [])) '$u']);
  }

  /// A file the notch wants sent to the PC (a screenshot's content:// address).
  static void Function(String uri)? onBeamFile;
  /// "Update" in the update notification.
  static void Function()? onInstallUpdate;
  /// How installing an update ended (when it didn't replace the app).
  static void Function(bool ok, String message)? onUpdateStatus;

  static void init() {
    _bridge.setMethodCallHandler((call) async {
      switch (call.method) {
        case 'offerAnswer':
          final args = Map<String, Object?>.from(call.arguments as Map);
          _answers.remove(args['id'])?.complete(args['accept'] == true);
        case 'open':
          onOpen?.call(call.arguments as String);
        case 'phoneStatus':
          onPhoneStatus?.call(Map<String, Object?>.from(call.arguments as Map));
        case 'shared':
          onShared?.call();
        case 'beamFile':
          onBeamFile?.call(call.arguments as String);
        case 'installUpdate':
          onInstallUpdate?.call();
        case 'updateStatus':
          final args = Map<String, Object?>.from(call.arguments as Map);
          onUpdateStatus?.call(args['ok'] == true, args['message'] as String? ?? '');
        case 'mute':
          final args = Map<String, Object?>.from(call.arguments as Map);
          onMute?.call(args['package'] as String, args['name'] as String? ?? args['package'] as String);
      }
    });
  }

  static void send(Map<String, Object?> event) => _bridge.invokeMethod('holmEvent', event).catchError((_) {});

  static void settings(NotchSettings s) => _bridge.invokeMethod('notchSettings', s.toJsonString()).catchError((_) {});

  static void preview(bool on, {bool open = false}) => send({'type': 'preview', 'on': on, 'open': open});

  /// Asks in the notch (the app isn't open); false after a minute without an answer, or without a notch.
  static Future<bool> ask(String sender, String summary, bool text) async {
    final status = await NotchLink.status();
    if (status['running'] != true) return false;
    final id = DateTime.now().microsecondsSinceEpoch.toString();
    final answer = Completer<bool>();
    _answers[id] = answer;
    send({'type': 'holm.offer', 'id': id, 'sender': sender, 'summary': summary, 'text': text});
    return answer.future.timeout(const Duration(seconds: 60), onTimeout: () {
      _answers.remove(id);
      send({'type': 'holm.offerGone', 'id': id});
      return false;
    });
  }

  static Future<Map<String, Object?>> status() async {
    try {
      final s = await _bridge.invokeMethod<Map>('status');
      return Map<String, Object?>.from(s ?? {});
    } catch (_) {
      return {};
    }
  }

  /// Hides Android's own notification pop-ups while the notch runs; true if Holm may (WRITE_SECURE_SETTINGS).
  static Future<bool> hidePopups(bool hide) async =>
      (await _bridge.invokeMethod<bool>('hidePopups', hide).catchError((_) => false)) ?? false;

  /// Shizuku's state: "missing", "stopped", "ask" or "ready" (ShizukuGrant.kt).
  static Future<String> shizuku() async =>
      (await _bridge.invokeMethod<String>('shizuku').catchError((_) => 'missing')) ?? 'missing';

  /// Lets Shizuku give Holm the permission to hide the pop-ups: "granted", "denied", "missing", "stopped" or "failed".
  static Future<String> unlockWithShizuku() async =>
      (await _bridge.invokeMethod<String>('shizukuUnlock').catchError((_) => 'failed')) ?? 'failed';

  /// Copies a content:// file into the app's cache, to send it; its path.
  static Future<String?> copyUri(String uri) => _bridge.invokeMethod<String>('copyUri', uri).catchError((_) => null);

  /// Asks for a runtime permission ("images", "calendar"); true if Holm has it.
  static Future<bool> askPermission(String which) async =>
      (await _system.invokeMethod<bool>('askPermission', which).catchError((_) => false)) ?? false;
  static Future<bool> hasPermission(String which) async =>
      (await _system.invokeMethod<bool>('hasPermission', which).catchError((_) => false)) ?? false;

  /// The apps one can open, for choosing their colour in the notch: package, name, icon (PNG).
  static Future<List<({String package, String name, Uint8List? icon})>> apps() async {
    final list = await _bridge.invokeMethod<List>('apps').catchError((_) => null) ?? const [];
    return [
      for (final a in list)
        if (a is Map) (package: '${a['package']}', name: '${a['name']}', icon: a['icon'] as Uint8List?),
    ];
  }

  /// The player plays, pauses or skips (asked from a PC).
  static void mediaControl(String action) => _bridge.invokeMethod('media', action).catchError((_) {});

  /// Clears one of the phone's notifications (closed on a PC).
  static void dismissNotification(String key) => _bridge.invokeMethod('dismissNotification', key).catchError((_) {});

  /// This app's version name ("1.2.0").
  static Future<String?> appVersionName() => _bridge.invokeMethod<String>('appVersionName').catchError((_) => null);

  /// This app's version code.
  static Future<int?> appVersion() => _bridge.invokeMethod<int>('appVersion').catchError((_) => null);
  /// Shows the "new version" notification (once per version).
  static void notifyUpdate(int code, String name) =>
      _bridge.invokeMethod('notifyUpdate', {'code': code, 'name': name}).catchError((_) {});
  /// Hands a downloaded APK to Android's installer: "started", or "permission" (Holm may not install apps yet).
  static Future<String> installUpdate(String path) async =>
      (await _bridge.invokeMethod<String>('installUpdate', path).catchError((_) => 'failed')) ?? 'failed';

  static Future<void> openAccessibility() => _system.invokeMethod('openAccessibility').catchError((_) {});
  static Future<void> openNotificationAccess() => _system.invokeMethod('openNotificationAccess').catchError((_) {});
  static Future<bool> bluetoothAllowed() async => (await _system.invokeMethod<bool>('bluetoothAllowed').catchError((_) => false)) ?? false;
  static Future<void> askBluetooth() => _system.invokeMethod('askBluetooth').catchError((_) {});
}
