import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:path_provider/path_provider.dart';
import 'package:pointycastle/digests/sha256.dart';

import 'notch/link.dart';

/// A newer Holm for Android, as the update server describes it (latest.json).
class UpdateInfo {
  final int code;
  final String name, file, sha256, notes;
  const UpdateInfo({required this.code, required this.name, required this.file, required this.sha256, required this.notes});

  static UpdateInfo? fromJson(Object? j) {
    if (j is! Map) return null;
    final code = j['versionCode'], name = j['versionName'], file = j['file'], sha = j['sha256'];
    if (code is! int || name is! String || file is! String || sha is! String) return null;
    // Only a plain file name on the same server: latest.json can't send the app anywhere else.
    if (!RegExp(r'^[A-Za-z0-9._-]+\.apk$').hasMatch(file) || !RegExp(r'^[0-9a-f]{64}$').hasMatch(sha)) return null;
    return UpdateInfo(code: code, name: name, file: file, sha256: sha, notes: (j['notes'] as String?) ?? '');
  }
}

/// Holm's own updates: asks Holm's update server every few hours, tells with a notification and a button, and
/// installs the new version itself (Android asks once to confirm). The server's certificate is pinned, and the
/// download must match the checksum from latest.json before it is installed.
class Updater extends ChangeNotifier {
  static final instance = Updater();

  static const host = '62.83.85.236', port = 8473;
  /// SHA-256 of the update server's certificate (DER): self-signed, so it is the only one trusted.
  static const pin = '6c141402e122fd685537e2a01b35cf65e2af6c4cc6c4c26782fff994e1b22d30';

  UpdateInfo? available;
  int? current;
  /// This app's version as people read it ("1.2.0").
  String? currentName;
  /// A check is running; the last one couldn't reach the server; when it last asked.
  bool checking = false, unreachable = false;
  DateTime? lastChecked;
  /// While downloading: 0..1.
  double? progress;
  String? error;
  bool installing = false;
  /// Test versions too (beta.json), whichever is newer.
  bool beta = false;
  /// Just updated: the version and what's new in it, shown once.
  UpdateInfo? whatsNew;
  Timer? _timer;

  void start() {
    if (_timer != null) return;
    NotchLink.onInstallUpdate = install;
    NotchLink.onUpdateStatus = (ok, message) {
      installing = false;
      progress = null;
      error = ok ? null : message;
      notifyListeners();
    };
    unawaited(_afterUpdate().then((_) => check()));
    _timer = Timer.periodic(const Duration(hours: 6), (_) => check());
  }

  Future<File> get _pendingFile async => File('${(await getApplicationSupportDirectory()).path}/update-pending.json');

  /// The version being installed: after the update, its notes are shown once.
  Future<void> _remember(UpdateInfo info) async {
    try {
      await (await _pendingFile).writeAsString(jsonEncode(
          {'versionCode': info.code, 'versionName': info.name, 'file': info.file, 'sha256': info.sha256, 'notes': info.notes}));
    } catch (_) {}
  }

  Future<void> _afterUpdate() async {
    try {
      final f = await _pendingFile;
      if (!await f.exists()) return;
      final info = UpdateInfo.fromJson(jsonDecode(await f.readAsString()));
      current ??= await NotchLink.appVersion();
      if (info == null || current == null) return;
      if (current! >= info.code) {
        await f.delete();
        if (info.notes.isNotEmpty) {
          whatsNew = info;
          notifyListeners();
        }
      }
    } catch (_) {}
  }

  void seenWhatsNew() {
    whatsNew = null;
    notifyListeners();
  }

  Future<UpdateInfo?> _fetch(HttpClient client, String index) async {
    final response = await (await client.getUrl(_url('/$index'))).close();
    if (response.statusCode != 200) {
      await response.drain<void>();
      return null;
    }
    return UpdateInfo.fromJson(jsonDecode(await response.transform(utf8.decoder).join()));
  }

  static String hex(List<int> bytes) => [for (final b in bytes) b.toRadixString(16).padLeft(2, '0')].join();

  static HttpClient _client() => HttpClient(context: SecurityContext(withTrustedRoots: false))
    ..connectionTimeout = const Duration(seconds: 15)
    ..badCertificateCallback = (cert, h, p) => h == host && p == port && hex(SHA256Digest().process(cert.der)) == pin;

  static Uri _url(String path) => Uri(scheme: 'https', host: host, port: port, path: path);

  Future<void> check() async {
    if (checking) return;
    checking = true;
    notifyListeners();
    final client = _client();
    try {
      current ??= await NotchLink.appVersion();
      currentName ??= await NotchLink.appVersionName();
      var info = await _fetch(client, 'latest.json');
      if (beta) {
        final test = await _fetch(client, 'beta.json');
        if (test != null && (info == null || test.code > info.code)) info = test;
      }
      final now = current ?? 1 << 30;
      final next = info != null && info.code > now ? info : null;
      if (next?.code != available?.code) {
        available = next;
        notifyListeners();
      }
      if (next != null) NotchLink.notifyUpdate(next.code, next.name);
      unreachable = info == null;
    } catch (_) {
      unreachable = true; // offline or the server is away: next time
    } finally {
      client.close(force: true);
      checking = false;
      lastChecked = DateTime.now();
      notifyListeners();
    }
  }

  /// Downloads the new version, checks it and hands it to Android's installer.
  Future<void> install() async {
    if (installing) return;
    // From the notification, maybe before the first check of this start.
    if (available == null) await check();
    final info = available;
    if (info == null) return;
    installing = true;
    progress = 0;
    error = null;
    notifyListeners();
    final client = _client();
    try {
      // Named by its checksum and renamed only once it matched: already here (say, from before Android asked to
      // allow installs), it is used as it is.
      final dir = (await getTemporaryDirectory()).path;
      final file = File('$dir/holm-update-${info.sha256}.apk');
      if (!await file.exists()) await _download(client, info, file);
      progress = null;
      notifyListeners();
      await _remember(info);
      final answer = await NotchLink.installUpdate(file.path);
      if (answer == 'permission') {
        error = 'Allow Holm to install apps, then tap Update again';
        installing = false;
      } else if (answer != 'started') {
        throw 'Android didn’t take the update';
      }
      // "started": Android asks to confirm, then replaces Holm (this process ends).
    } catch (e) {
      error = '$e';
      installing = false;
      progress = null;
    } finally {
      client.close(force: true);
      notifyListeners();
    }
  }

  Future<void> _download(HttpClient client, UpdateInfo info, File target) async {
    final response = await (await client.getUrl(_url('/${info.file}'))).close();
    if (response.statusCode != 200) throw 'The server answered ${response.statusCode}';
    final total = response.contentLength;
    final file = File('${target.path}.part');
    // Older downloads go: one APK at a time in the cache.
    await for (final old in target.parent.list()) {
      if (old is File && old.uri.pathSegments.last.startsWith('holm-update')) await old.delete();
    }
    {
      final sink = file.openWrite();
      final digest = SHA256Digest();
      var got = 0;
      await for (final chunk in response) {
        final bytes = chunk is Uint8List ? chunk : Uint8List.fromList(chunk);
        digest.update(bytes, 0, bytes.length);
        sink.add(bytes);
        got += bytes.length;
        if (total > 0) {
          progress = got / total;
          notifyListeners();
        }
      }
      await sink.close();
      final out = Uint8List(digest.digestSize);
      digest.doFinal(out, 0);
      if (hex(out) != info.sha256) {
        await file.delete();
        throw 'The download was damaged';
      }
      await file.rename(target.path);
    }
  }
}
