// The Holm account (Holm.Core/Account), as far as the phone needs it: sign in, open the account's vault and read the
// friends identity, the friends list and the profile that Holm on the PC keeps there. The phone only reads; the PCs
// stay the ones that write and merge the account.
//
// As on the desktop the password never leaves the device: from it and the email come a sign-in secret (what Firebase
// gets as the password) and a vault key that opens the account's data key. Everything in Firestore is sealed.
import 'dart:convert';
import 'dart:io';
import 'dart:isolate';
import 'dart:typed_data';

import 'package:pointycastle/export.dart';
import 'package:unorm_dart/unorm_dart.dart' as unorm;

import 'crypto.dart';

/// The Firebase project "Holm Accounts" (Firebase.cs). The web API key only names the project; what a signed-in user
/// may read is decided by the Firestore rules (firebase/firestore.rules).
const firebaseApiKey = 'AIzaSyBlVxpL7NiLGM-vDQ_VJqnHj8tWrP8qtmM';
const firebaseProject = 'holm-accounts';

class AccountException implements Exception {
  final String message;
  final String? code;
  const AccountException(this.message, [this.code]);
  @override
  String toString() => message;
}

/// The keys made from an account's email and password (AccountVault.cs, AccountKeys).
class AccountKeys {
  static const rounds = 600000;
  final String signInSecret;
  final Uint8List vaultKey;
  const AccountKeys(this.signInSecret, this.vaultKey);

  static String normalizeEmail(String email) => unorm.nfc(email.trim()).toLowerCase();

  /// Slow on purpose (600 000 rounds of PBKDF2): runs in its own isolate.
  static Future<AccountKeys> derive(String email, String password) async {
    final parts = await Isolate.run(() => _derive(email, password));
    return AccountKeys(parts.$1, parts.$2);
  }

  static (String, Uint8List) _derive(String email, String password) {
    final master = pbkdf2Sha256(utf8.encode(unorm.nfc(password)), utf8.encode('holm-account-v1:${normalizeEmail(email)}'), rounds, 32);
    final signIn = hkdfExpand(master, utf8.encode('holm sign-in'));
    final vault = hkdfExpand(master, utf8.encode('holm vault'));
    master.fillRange(0, master.length, 0);
    return (base64Url(signIn), vault);
  }
}

/// HKDF-Expand with SHA-256 for one block (32 bytes), as .NET's HKDF.Expand with the master key as the PRK.
Uint8List hkdfExpand(List<int> prk, List<int> info) {
  final mac = HMac(SHA256Digest(), 64)..init(KeyParameter(Uint8List.fromList(prk)));
  return mac.process(Uint8List.fromList([...info, 1]));
}

/// PBKDF2-HMAC-SHA256. HMAC's inner and outer pads are hashed once, so each round costs two SHA-256 blocks.
Uint8List pbkdf2Sha256(List<int> password, List<int> salt, int rounds, int length) {
  final key = password.length > 64 ? sha256(password) : Uint8List.fromList(password);
  final ipad = Uint8List(64), opad = Uint8List(64);
  for (var i = 0; i < 64; i++) {
    final k = i < key.length ? key[i] : 0;
    ipad[i] = k ^ 0x36;
    opad[i] = k ^ 0x5c;
  }
  final inner = _Sha256.start(ipad), outer = _Sha256.start(opad);
  final out = Uint8List(length);
  for (var block = 1, pos = 0; pos < length; block++) {
    // U1 = HMAC(P, S || INT(block)); the salt may be longer than a block, so the first round uses the general path.
    final mac = HMac(SHA256Digest(), 64)..init(KeyParameter(Uint8List.fromList(password)));
    var u = mac.process(Uint8List.fromList([...salt, block >> 24, (block >> 16) & 0xff, (block >> 8) & 0xff, block & 0xff]));
    final t = Uint32List(8), w = Uint32List(8);
    _toWords(u, w);
    t.setAll(0, w);
    for (var r = 1; r < rounds; r++) {
      inner.hash32(w, w);
      outer.hash32(w, w);
      for (var i = 0; i < 8; i++) {
        t[i] ^= w[i];
      }
    }
    final bytes = Uint8List(32);
    for (var i = 0; i < 8; i++) {
      bytes[i * 4] = t[i] >> 24;
      bytes[i * 4 + 1] = (t[i] >> 16) & 0xff;
      bytes[i * 4 + 2] = (t[i] >> 8) & 0xff;
      bytes[i * 4 + 3] = t[i] & 0xff;
    }
    final take = (length - pos).clamp(0, 32);
    out.setRange(pos, pos + take, bytes);
    pos += take;
  }
  return out;
}

void _toWords(List<int> b, Uint32List w) {
  for (var i = 0; i < 8; i++) {
    w[i] = (b[i * 4] << 24) | (b[i * 4 + 1] << 16) | (b[i * 4 + 2] << 8) | b[i * 4 + 3];
  }
}

/// SHA-256 that starts from the state after one 64-byte block and hashes one more 32-byte message (with its padding):
/// exactly the inner and outer hashes of HMAC in PBKDF2's later rounds.
class _Sha256 {
  static const _k = [
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, //
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
  ];
  static final _kw = Uint32List.fromList(_k);

  final Uint32List _start = Uint32List(8);
  final Uint32List _w = Uint32List(64);
  final Uint32List _h = Uint32List(8);

  _Sha256.start(Uint8List block) {
    _start.setAll(0, [0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19]);
    for (var i = 0; i < 16; i++) {
      _w[i] = (block[i * 4] << 24) | (block[i * 4 + 1] << 16) | (block[i * 4 + 2] << 8) | block[i * 4 + 3];
    }
    _h.setAll(0, _start);
    _compress();
    _start.setAll(0, _h);
  }

  /// [out] = SHA-256(pad-block || [msg]) for a 32-byte message given as 8 words. [out] may be [msg].
  void hash32(Uint32List msg, Uint32List out) {
    final w = _w;
    for (var i = 0; i < 8; i++) {
      w[i] = msg[i];
    }
    w[8] = 0x80000000;
    for (var i = 9; i < 15; i++) {
      w[i] = 0;
    }
    w[15] = (64 + 32) * 8;
    _h.setAll(0, _start);
    _compress();
    out.setAll(0, _h);
  }

  void _compress() {
    final w = _w, k = _kw, h = _h;
    for (var i = 16; i < 64; i++) {
      final x = w[i - 15], y = w[i - 2];
      final s0 = ((x >> 7) | (x << 25)) ^ ((x >> 18) | (x << 14)) ^ (x >> 3);
      final s1 = ((y >> 17) | (y << 15)) ^ ((y >> 19) | (y << 13)) ^ (y >> 10);
      w[i] = (w[i - 16] + s0 + w[i - 7] + s1) & 0xffffffff;
    }
    var a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
    for (var i = 0; i < 64; i++) {
      final s1 = (((e >> 6) | (e << 26)) ^ ((e >> 11) | (e << 21)) ^ ((e >> 25) | (e << 7))) & 0xffffffff;
      final ch = ((e & f) ^ (~e & g)) & 0xffffffff;
      final t1 = (hh + s1 + ch + k[i] + w[i]) & 0xffffffff;
      final s0 = (((a >> 2) | (a << 30)) ^ ((a >> 13) | (a << 19)) ^ ((a >> 22) | (a << 10))) & 0xffffffff;
      final maj = (a & b) ^ (a & c) ^ (b & c);
      final t2 = (s0 + maj) & 0xffffffff;
      hh = g;
      g = f;
      f = e;
      e = (d + t1) & 0xffffffff;
      d = c;
      c = b;
      b = a;
      a = (t1 + t2) & 0xffffffff;
    }
    h[0] = (h[0] + a) & 0xffffffff;
    h[1] = (h[1] + b) & 0xffffffff;
    h[2] = (h[2] + c) & 0xffffffff;
    h[3] = (h[3] + d) & 0xffffffff;
    h[4] = (h[4] + e) & 0xffffffff;
    h[5] = (h[5] + f) & 0xffffffff;
    h[6] = (h[6] + g) & 0xffffffff;
    h[7] = (h[7] + hh) & 0xffffffff;
  }
}

/// Sealed account data (AccountVault.cs): "v1." and base64url(nonce | tag | data), bound to a context.
class AccountVault {
  static Uint8List open(Uint8List key, String sealedText, String context) {
    if (!sealedText.startsWith('v1.')) throw const AccountException('The account data is in an unknown format.');
    final sealed = fromBase64Url(sealedText.substring(3));
    if (sealed.length < 28) throw const AccountException('The account data is damaged.');
    return aesGcm(false, key, sealed.sublist(0, 12), concat([sealed.sublist(28), sealed.sublist(12, 28)]), utf8.encode(context));
  }

  static String seal(Uint8List key, List<int> plain, String context) {
    final nonce = randomBytes(12);
    final out = aesGcm(true, key, nonce, Uint8List.fromList(plain), utf8.encode(context));
    return 'v1.${base64Url(concat([nonce, out.sublist(out.length - 16), out.sublist(0, out.length - 16)]))}';
  }

  static Uint8List unwrapDataKey(AccountKeys keys, String uid, String wrapped) {
    try {
      return open(keys.vaultKey, wrapped, 'vault:$uid');
    } on AccountException {
      rethrow;
    } catch (_) {
      throw const AccountException("The account's key couldn't be opened. Was the password changed on another device?");
    }
  }

  static Uint8List openSection(Uint8List dataKey, String uid, String section, String sealedText) =>
      open(dataKey, sealedText, 'data:$uid/$section');
}

/// The private key in a PKCS#8 PrivateKeyInfo with an ECPrivateKey inside (what .NET's ExportPkcs8PrivateKey writes).
BigInt ecPrivateKeyFromPkcs8(List<int> der) {
  final outer = _Der(der).sequence();
  outer.integer(); // version
  outer.skip(); // AlgorithmIdentifier
  final inner = _Der(outer.octets()).sequence();
  inner.integer(); // version 1
  final d = inner.octets();
  if (d.isEmpty || d.length > 32) throw const FormatException('Not a P-256 key.');
  return bytesBigInt(d);
}

class _Der {
  final List<int> _b;
  int _pos = 0;
  _Der(this._b);

  (int, List<int>) _next() {
    if (_pos + 2 > _b.length) throw const FormatException('Damaged key.');
    final tag = _b[_pos++];
    var len = _b[_pos++];
    if (len & 0x80 != 0) {
      final n = len & 0x7f;
      if (n == 0 || n > 3) throw const FormatException('Damaged key.');
      len = 0;
      for (var i = 0; i < n; i++) {
        len = (len << 8) | _b[_pos++];
      }
    }
    if (_pos + len > _b.length) throw const FormatException('Damaged key.');
    final value = _b.sublist(_pos, _pos + len);
    _pos += len;
    return (tag, value);
  }

  _Der sequence() {
    final (tag, value) = _next();
    if (tag != 0x30) throw const FormatException('Damaged key.');
    return _Der(value);
  }

  List<int> octets() {
    final (tag, value) = _next();
    if (tag != 0x04) throw const FormatException('Damaged key.');
    return value;
  }

  void integer() {
    final (tag, _) = _next();
    if (tag != 0x02) throw const FormatException('Damaged key.');
  }

  void skip() => _next();
}

/// A signed-in session: who, and the tokens to act for them (Firebase.cs).
class FirebaseSession {
  final String uid;
  final String idToken;
  final String refreshToken;
  const FirebaseSession(this.uid, this.idToken, this.refreshToken);
}

/// Firebase Authentication and Firestore over their REST APIs, read-only for the phone.
class Firebase {
  static final HttpClient _http = HttpClient()
    ..userAgent = 'Holm-Account/1'
    ..connectionTimeout = const Duration(seconds: 15);

  static Future<FirebaseSession> signIn(String email, String signInSecret) async {
    final j = await _send('POST', 'https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=$firebaseApiKey',
        jsonBody: {'email': email, 'password': signInSecret, 'returnSecureToken': true});
    return FirebaseSession(j['localId'] as String, j['idToken'] as String, j['refreshToken'] as String);
  }

  /// A fresh ID token (they last an hour) from the refresh token.
  static Future<FirebaseSession> refresh(String refreshToken) async {
    final j = await _send('POST', 'https://securetoken.googleapis.com/v1/token?key=$firebaseApiKey',
        form: 'grant_type=refresh_token&refresh_token=${Uri.encodeQueryComponent(refreshToken)}');
    return FirebaseSession(j['user_id'] as String, j['id_token'] as String, j['refresh_token'] as String);
  }

  /// One string field of the document at [path] ("users/uid/sync/identity"), or null if there is none.
  static Future<String?> field(String path, String name, String idToken) async {
    try {
      final j = await _send('GET',
          'https://firestore.googleapis.com/v1/projects/$firebaseProject/databases/(default)/documents/$path', idToken: idToken);
      return ((j['fields'] as Map<String, dynamic>?)?[name] as Map<String, dynamic>?)?['stringValue'] as String?;
    } on AccountException catch (e) {
      if (e.code == 'NOT_FOUND') return null;
      rethrow;
    }
  }

  static Future<Map<String, dynamic>> _send(String method, String url,
      {Map<String, dynamic>? jsonBody, String? form, String? idToken}) async {
    HttpClientResponse res;
    String text;
    try {
      final req = await _http.openUrl(method, Uri.parse(url));
      if (idToken != null) req.headers.set(HttpHeaders.authorizationHeader, 'Bearer $idToken');
      if (jsonBody != null) {
        req.headers.contentType = ContentType.json;
        req.add(utf8.encode(jsonEncode(jsonBody)));
      } else if (form != null) {
        req.headers.contentType = ContentType('application', 'x-www-form-urlencoded');
        req.add(utf8.encode(form));
      }
      res = await req.close().timeout(const Duration(seconds: 30));
      text = await utf8.decodeStream(res).timeout(const Duration(seconds: 30));
    } catch (_) {
      throw const AccountException("Couldn't reach the Holm account service. Check the internet connection.");
    }
    Map<String, dynamic> j = {};
    try {
      if (text.isNotEmpty) j = jsonDecode(text) as Map<String, dynamic>;
    } catch (_) {}
    if (res.statusCode >= 200 && res.statusCode < 300) return j;
    final code = _code(j, res.statusCode);
    throw AccountException(_describe(code), code);
  }

  static String _code(Map<String, dynamic> j, int status) {
    final error = j['error'];
    if (error is Map<String, dynamic>) {
      final message = error['message'];
      if (message is String) {
        final first = message.split(' : ').first.trim();
        if (RegExp(r'^[A-Z_]+$').hasMatch(first)) return first;
      }
      final state = error['status'];
      if (state is String) return state;
    }
    return status == 404 ? 'NOT_FOUND' : '$status';
  }

  static String _describe(String code) => switch (code) {
        'INVALID_LOGIN_CREDENTIALS' || 'EMAIL_NOT_FOUND' || 'INVALID_PASSWORD' => 'The email or the password is wrong.',
        'INVALID_EMAIL' => "That isn't an email address.",
        'USER_DISABLED' => 'This Holm account has been switched off.',
        'TOO_MANY_ATTEMPTS_TRY_LATER' => 'Too many tries. Wait a few minutes and try again.',
        'TOKEN_EXPIRED' || 'INVALID_REFRESH_TOKEN' || 'INVALID_ID_TOKEN' || 'USER_NOT_FOUND' => 'You were signed out. Sign in again.',
        'PERMISSION_DENIED' || 'UNAUTHENTICATED' => 'The Holm account service refused this. Sign in again.',
        _ => 'The Holm account service answered with an error ($code).',
      };
}

/// What the phone takes from the account: the identity, the friends and the profile name.
class AccountData {
  final String? topic;
  final BigInt? key;
  final String? server;
  final String? profileName;
  final List<Map<String, dynamic>> friends;
  final Set<String> removed;
  const AccountData({this.topic, this.key, this.server, this.profileName, this.friends = const [], this.removed = const {}});

  bool get hasIdentity => topic != null && key != null;

  /// Reads and opens the account's sections. [dataKey] opens them; the vault isn't read again.
  static Future<AccountData> fetch(String uid, String idToken, Uint8List dataKey) async {
    Future<Map<String, dynamic>?> section(String name) async {
      final sealed = await Firebase.field('users/$uid/sync/$name', 'data', idToken);
      if (sealed == null) return null;
      try {
        return jsonDecode(utf8.decode(AccountVault.openSection(dataKey, uid, name, sealed))) as Map<String, dynamic>;
      } catch (_) {
        return null; // damaged, or from another account: left out
      }
    }

    final identity = await section('identity');
    final friends = await section('friends');
    final profile = await section('profile');
    BigInt? key;
    final pkcs8 = identity?['Key'] as String?;
    if (pkcs8 != null && pkcs8.isNotEmpty) key = ecPrivateKeyFromPkcs8(base64Decode(pkcs8));
    final topic = identity?['Topic'] as String?;
    return AccountData(
      topic: topic != null && RegExp(r'^[A-Za-z0-9_-]{1,64}$').hasMatch(topic) ? topic : null,
      key: key,
      server: identity?['Server'] as String?,
      profileName: (profile?['Name'] as String?)?.trim(),
      friends: [for (final f in (friends?['Friends'] as List? ?? [])) if (f is Map<String, dynamic>) f],
      removed: {...((friends?['Removed'] as Map<String, dynamic>?) ?? {}).keys},
    );
  }
}
