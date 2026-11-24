// Holm.Core/Sharing/Friends.cs and FriendInvite.cs in Dart: friend codes, notes sealed end to end, the inbox on
// Holm's relay (with ntfy.sh as the fallback every Holm also follows), and short invite codes.

import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'crypto.dart';

const relayServer = 'https://relay.holmnotch.ch';
const legacyServer = 'https://ntfy.sh';
const _userAgent = 'Holm-Friends/1';

class Friend {
  String name;
  final String topic;
  final String key; // base64url SubjectPublicKeyInfo
  final String? server; // null: ntfy.sh, as a card without a server means

  Friend({required this.name, required this.topic, required this.key, this.server});

  String get serverUrl => server ?? legacyServer;

  Map<String, dynamic> toJson() => {'name': name, 'topic': topic, 'key': key, 'server': server};
  factory Friend.fromJson(Map<String, dynamic> j) =>
      Friend(name: j['name'] as String, topic: j['topic'] as String, key: j['key'] as String, server: j['server'] as String?);
}

class Identity {
  final String name;
  final String topic;
  final EcKey key;

  Identity(this.name, this.topic, this.key);

  factory Identity.create(String name) {
    const alphabet = 'abcdefghijklmnopqrstuvwxyz234567';
    final topic = 'wnf-${randomBytes(26).map((b) => alphabet[b & 31]).join()}';
    return Identity(name, topic, EcKey.generate());
  }

  Identity withName(String name) => Identity(name, topic, key);

  String get publicKey => base64Url(key.spki());

  /// What you give a friend so they can add you: "WNF1." and base64url(JSON), as Holm's desktop app writes it.
  String get friendCode {
    final card = {'n': name, 't': topic, 'k': publicKey, 's': relayServer};
    return 'WNF1.${base64Url(utf8.encode(jsonEncode(card)))}';
  }

  Map<String, dynamic> toJson() => {'name': name, 'topic': topic, 'd': key.d.toRadixString(16)};
  factory Identity.fromJson(Map<String, dynamic> j) =>
      Identity(j['name'] as String, j['topic'] as String, EcKey.fromPrivate(BigInt.parse(j['d'] as String, radix: 16)));
}

final _codeInText = RegExp(r'WNF1\.[A-Za-z0-9_-]{40,2000}');

/// Reads a friend code anywhere in the text; null if there is none or it is damaged.
Friend? parseFriendCode(String? text) {
  if (text == null || text.length > 10000) return null;
  final match = _codeInText.firstMatch(text);
  if (match == null) return null;
  try {
    final card = jsonDecode(utf8.decode(fromBase64Url(match.group(0)!.substring(5)))) as Map<String, dynamic>;
    final t = card['t'] as String?, k = card['k'] as String?;
    if (t == null || k == null || !RegExp(r'^[A-Za-z0-9_-]{1,64}$').hasMatch(t)) return null;
    parseSpki(fromBase64Url(k));
    String? server = (card['s'] as String?)?.trim();
    if (server != null) {
      server = server.replaceAll(RegExp(r'/+$'), '');
      final uri = Uri.tryParse(server);
      if (uri == null || !(uri.scheme == 'https' || uri.scheme == 'http')) return null;
      if (server == legacyServer) server = null;
    }
    var name = ((card['n'] as String?) ?? '').runes.where((r) => r >= 0x20).map(String.fromCharCode).join().trim();
    if (name.isEmpty) name = 'Friend';
    if (name.length > 40) name = name.substring(0, 40);
    return Friend(name: name, topic: t, key: k, server: server);
  } catch (_) {
    return null;
  }
}

/// A note as it travels, keyed by the one-letter names of Friends.cs's NoteDto.
class Note {
  final String kind; // chat, react, request, response, cancel-request, removed, ping, here, wave, ...
  final String from;
  final int sent;
  final Map<String, dynamic> fields;
  Note(this.kind, this.from, this.sent, [this.fields = const {}]);

  String? get text {
    final m = fields['M'];
    if (m is! String) return null;
    try {
      return utf8.decode(base64Decode(m));
    } catch (_) {
      return null;
    }
  }

  String? get messageId => fields['I'] as String?;
  String? get requestId => fields['R'] as String?;
  String? get code => fields['C'] as String?;
  bool? get accepted => fields['A'] as bool?;
  bool get isGif => fields['G'] == true;
}

int unixNow() => DateTime.now().millisecondsSinceEpoch ~/ 1000;

class Notes {
  static const prefix = 'wnf1 ';
  static final _label = utf8.encode('WinNotch friends v1');

  static List<int> _sharedKey(EcKey mine, String theirKey) =>
      mine.deriveKeyFromHash(parseSpki(fromBase64Url(theirKey)), _label);

  /// Seals a note for [to]. [from] is the name in it: our name for friends, the device's name between our devices.
  static String seal(Identity me, Friend to, String kind, Map<String, dynamic> fields, {String? from}) {
    final dto = <String, dynamic>{'K': kind, 'F': from ?? me.name, 'T': unixNow(), ...fields}..removeWhere((_, v) => v == null);
    final plain = utf8.encode(jsonEncode(dto));
    final nonce = randomBytes(12);
    final sealed = aesGcm(true, Uint8List.fromList(_sharedKey(me.key, to.key)), nonce, Uint8List.fromList(plain), utf8.encode(to.topic));
    // Holm lays a note out as nonce | tag | ciphertext; GCM gives ciphertext | tag.
    final ct = sealed.sublist(0, sealed.length - 16), tag = sealed.sublist(sealed.length - 16);
    return '$prefix${me.publicKey} ${base64Url(concat([nonce, tag, ct]))}';
  }

  static String chatText(String text) => base64Encode(utf8.encode(text));

  /// Opens a note from our inbox; null for anything that isn't a valid note for us. Returns the sender's key too.
  static (Note, String)? open(Identity me, String text) {
    if (!text.startsWith(prefix) || text.length > 4096) return null;
    final parts = text.substring(prefix.length).split(' ');
    if (parts.length != 2) return null;
    try {
      final sealed = fromBase64Url(parts[1]);
      if (sealed.length < 12 + 16 + 2) return null;
      final nonce = sealed.sublist(0, 12), tag = sealed.sublist(12, 28), ct = sealed.sublist(28);
      final plain = aesGcm(false, Uint8List.fromList(_sharedKey(me.key, parts[0])), nonce, concat([ct, tag]), utf8.encode(me.topic));
      final dto = jsonDecode(utf8.decode(plain)) as Map<String, dynamic>;
      final from = ((dto['F'] as String?) ?? '').runes.where((r) => r >= 0x20).take(40).map(String.fromCharCode).join().trim();
      final kind = dto['K'] as String?;
      if (kind == null) return null;
      return (Note(kind, from, (dto['T'] as num?)?.toInt() ?? 0, dto), parts[0]);
    } catch (_) {
      return null;
    }
  }
}

/// Short invite codes like HOLM-7KQ2X-9XF4M (FriendInvite.cs).
class Invite {
  static const _alphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
  static final _salt = ascii.encode('Holm friend invite v1');
  static final _inText = RegExp(r'(?<![A-Za-z0-9])HOLM[-\s]?([0-9A-Za-z]{5})[-\s]?([0-9A-Za-z]{5})(?![A-Za-z0-9])', caseSensitive: false);

  static String? find(String? text) {
    if (text == null || text.trim().isEmpty || text.length > 10000) return null;
    String raw;
    final m = _inText.firstMatch(text);
    if (m != null) {
      raw = m.group(1)! + m.group(2)!;
    } else {
      raw = text.trim().replaceAll('-', '').replaceAll(' ', '');
      if (raw.toUpperCase().startsWith('HOLM')) raw = raw.substring(4);
      if (raw.length != 10) return null;
    }
    final chars = StringBuffer();
    for (final ch in raw.toUpperCase().split('')) {
      final c = switch (ch) { 'O' => '0', 'I' || 'L' => '1', _ => ch };
      if (!_alphabet.contains(c)) return null;
      chars.write(c);
    }
    final s = chars.toString();
    return 'HOLM-${s.substring(0, 5)}-${s.substring(5)}';
  }

  static List<int> _derive(String code, String purpose, int length) =>
      hkdf(ascii.encode(code), length, _salt, ascii.encode(purpose));

  static String topic(String code) {
    const lower = 'abcdefghijklmnopqrstuvwxyz234567';
    return 'holm-inv-${_derive(code, 'topic', 20).map((b) => lower[b & 31]).join()}';
  }

  static String proof(String code) => toHex(_derive(code, 'proof', 16));

  static Friend? open(String code, String text) {
    if (!text.startsWith('hinv1 ') || text.length > 4096) return null;
    try {
      final sealed = fromBase64Url(text.substring(6));
      if (sealed.length < 30) return null;
      final plain = aesGcm(false, Uint8List.fromList(_derive(code, 'key', 32)), sealed.sublist(0, 12),
          concat([sealed.sublist(28), sealed.sublist(12, 28)]), ascii.encode(topic(code)));
      final card = jsonDecode(utf8.decode(plain)) as Map<String, dynamic>;
      if (((card['E'] as num?)?.toInt() ?? 0) < unixNow()) return null;
      return parseFriendCode(card['C'] as String?);
    } catch (_) {
      return null;
    }
  }
}

/// Leaving notes in inboxes and following our own.
class Inbox {
  static final HttpClient _http = HttpClient()
    ..userAgent = _userAgent
    ..connectionTimeout = const Duration(seconds: 15);

  static Future<void> post(Friend to, String note, {bool keep = true}) async {
    try {
      await _postTo(to.serverUrl, to.topic, note, keep);
    } on IOException {
      if (to.serverUrl != relayServer) rethrow;
      await _postTo(legacyServer, to.topic, note, keep); // a Holm that follows the relay follows ntfy too
    }
  }

  static Future<void> _postTo(String server, String topic, String note, bool keep) async {
    try {
      final req = await _http.postUrl(Uri.parse('$server/${Uri.encodeComponent(topic)}'));
      req.headers.contentType = ContentType('text', 'plain', charset: 'utf-8');
      if (!keep) req.headers.set('Cache', 'no');
      req.add(utf8.encode(note));
      final res = await req.close().timeout(const Duration(seconds: 20));
      await res.drain<void>();
      if (res.statusCode < 200 || res.statusCode > 299) {
        throw HttpException('${Uri.parse(server).host} refused the note (${res.statusCode})');
      }
    } on TimeoutException {
      throw HttpException("Couldn't reach ${Uri.parse(server).host}");
    } on SocketException {
      throw HttpException("Couldn't reach ${Uri.parse(server).host}");
    }
  }

  /// The JSON lines kept in an inbox since [since] (a Unix time or "12h").
  static Future<List<InboxEvent>> poll(String server, String topic, String since) async {
    final req = await _http.getUrl(Uri.parse('$server/${Uri.encodeComponent(topic)}/json?poll=1&since=$since'));
    final res = await req.close().timeout(const Duration(seconds: 20));
    final body = await res.transform(utf8.decoder).join();
    if (res.statusCode != 200) throw HttpException('${Uri.parse(server).host} answered ${res.statusCode}');
    return body.split('\n').map(InboxEvent.tryParse).whereType<InboxEvent>().toList();
  }

  /// Follows our inbox on the relay with a WebSocket: kept notes since [since], then each new one.
  static Future<WebSocket> follow(String topic, int? since) => WebSocket.connect(
        '${relayServer.replaceFirst('https', 'wss')}/${Uri.encodeComponent(topic)}/ws${since != null ? '?since=$since' : ''}',
        headers: {'User-Agent': _userAgent},
      ).timeout(const Duration(seconds: 20));

  static Future<Friend?> fetchInvite(String code) async {
    final topic = Invite.topic(code);
    final results = await Future.wait([
      for (final s in [relayServer, legacyServer])
        poll(s, topic, '12h').then<List<InboxEvent>?>((v) => v).catchError((_) => null),
    ]);
    if (results.every((r) => r == null)) throw const HttpException("Couldn't reach the invite server");
    final cards = [for (final r in results) ...?r].where((e) => e.isMessage && e.text != null).toList().reversed;
    for (final e in cards) {
      final friend = Invite.open(code, e.text!);
      if (friend != null) return friend;
    }
    return null;
  }
}

class InboxEvent {
  final bool isMessage;
  final int time;
  final String? id;
  final String? text;
  InboxEvent(this.isMessage, this.time, this.id, this.text);

  static InboxEvent? tryParse(String line) {
    if (line.trim().isEmpty) return null;
    try {
      final j = jsonDecode(line);
      if (j is! Map) return null;
      return InboxEvent(j['event'] == 'message', (j['time'] as num?)?.toInt() ?? 0, j['id'] as String?, j['message'] as String?);
    } catch (_) {
      return null;
    }
  }
}
