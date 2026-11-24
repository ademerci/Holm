// The cryptography Holm's desktop app uses (Holm.Core/Sharing), byte for byte: P-256 keys exchanged as
// SubjectPublicKeyInfo, .NET's DeriveKeyFromHash (SHA-256 over prepend || Z || append), AES-256-GCM, HKDF-SHA256.

import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:pointycastle/export.dart';

final _curve = ECCurve_secp256r1();
final _secure = Random.secure();

/// The DER header of a P-256 SubjectPublicKeyInfo with an uncompressed point, as .NET exports it (91 bytes in all).
final Uint8List _spkiPrefix = _hex('3059301306072a8648ce3d020106082a8648ce3d030107034200');

Uint8List _hex(String s) =>
    Uint8List.fromList([for (var i = 0; i < s.length; i += 2) int.parse(s.substring(i, i + 2), radix: 16)]);

String toHex(List<int> bytes) => bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();

Uint8List randomBytes(int n) => Uint8List.fromList(List.generate(n, (_) => _secure.nextInt(256)));

SecureRandom _fortuna() => FortunaRandom()..seed(KeyParameter(randomBytes(32)));

Uint8List sha256(List<int> data) => SHA256Digest().process(Uint8List.fromList(data));

Uint8List concat(List<List<int>> parts) {
  final b = BytesBuilder(copy: false);
  for (final p in parts) {
    b.add(p);
  }
  return b.takeBytes();
}

Uint8List bigIntBytes(BigInt value, int length) {
  final out = Uint8List(length);
  var v = value;
  for (var i = length - 1; i >= 0; i--) {
    out[i] = (v & BigInt.from(0xff)).toInt();
    v >>= 8;
  }
  return out;
}

BigInt bytesBigInt(List<int> bytes) {
  var v = BigInt.zero;
  for (final b in bytes) {
    v = (v << 8) | BigInt.from(b);
  }
  return v;
}

/// A P-256 key pair.
class EcKey {
  final ECPrivateKey privateKey;
  final ECPublicKey publicKey;

  EcKey._(this.privateKey, this.publicKey);

  factory EcKey.generate() {
    final gen = ECKeyGenerator()..init(ParametersWithRandom(ECKeyGeneratorParameters(_curve), _fortuna()));
    final pair = gen.generateKeyPair();
    return EcKey._(pair.privateKey, pair.publicKey);
  }

  factory EcKey.fromPrivate(BigInt d) {
    final q = (_curve.G * d)!;
    return EcKey._(ECPrivateKey(d, _curve), ECPublicKey(q, _curve));
  }

  BigInt get d => privateKey.d!;

  /// The public key as SubjectPublicKeyInfo (what .NET's ExportSubjectPublicKeyInfo gives).
  Uint8List spki() => concat([_spkiPrefix, publicKey.Q!.getEncoded(false)]);

  /// The raw ECDH secret Z (the shared point's x coordinate, 32 bytes).
  Uint8List agree(ECPublicKey theirs) {
    final agreement = ECDHBasicAgreement()..init(privateKey);
    return bigIntBytes(agreement.calculateAgreement(theirs), 32);
  }

  /// .NET's ECDiffieHellman.DeriveKeyFromHash(SHA256, prepend, append).
  Uint8List deriveKeyFromHash(ECPublicKey theirs, List<int> prepend, [List<int> append = const []]) =>
      sha256(concat([prepend, agree(theirs), append]));
}

/// Reads a P-256 SubjectPublicKeyInfo; throws [FormatException] for anything else or a point off the curve.
ECPublicKey parseSpki(List<int> bytes) {
  if (bytes.length != 91) throw const FormatException('Bad key.');
  for (var i = 0; i < _spkiPrefix.length; i++) {
    if (bytes[i] != _spkiPrefix[i]) throw const FormatException('Bad key.');
  }
  final point = _curve.curve.decodePoint(bytes.sublist(_spkiPrefix.length));
  if (point == null || point.isInfinity) throw const FormatException('Bad key.');
  final x = point.x!, y = point.y!;
  final a = _curve.curve.a!, b = _curve.curve.b!;
  if (y * y != x * x * x + a * x + b) throw const FormatException('Bad key.');
  return ECPublicKey(point, _curve);
}

/// AES-256-GCM with a 16-byte tag. Encrypting returns ciphertext || tag; decrypting takes the same and throws
/// [InvalidCipherTextException] when the tag doesn't match.
Uint8List aesGcm(bool encrypt, Uint8List key, Uint8List nonce, Uint8List input, [List<int>? aad]) {
  final cipher = GCMBlockCipher(AESEngine())
    ..init(encrypt, AEADParameters(KeyParameter(key), 128, nonce, Uint8List.fromList(aad ?? const [])));
  return cipher.process(input);
}

Uint8List hkdf(List<int> ikm, int length, List<int> salt, List<int> info) {
  final d = HKDFKeyDerivator(SHA256Digest())
    ..init(HkdfParameters(Uint8List.fromList(ikm), length, Uint8List.fromList(salt), Uint8List.fromList(info)));
  final out = Uint8List(length);
  d.deriveKey(null, 0, out, 0);
  return out;
}

String base64Url(List<int> bytes) => base64UrlEncode(bytes).replaceAll('=', '');

Uint8List fromBase64Url(String text) {
  final s = text.replaceAll('-', '+').replaceAll('_', '/');
  return base64Decode(s.padRight(s.length + (4 - s.length % 4) % 4, '='));
}

/// A random GUID as .NET writes it with "D" (8-4-4-4-12, lower case).
String newGuid() {
  final b = randomBytes(16);
  b[6] = (b[6] & 0x0f) | 0x40;
  b[8] = (b[8] & 0x3f) | 0x80;
  final h = toHex(b);
  return '${h.substring(0, 8)}-${h.substring(8, 12)}-${h.substring(12, 16)}-${h.substring(16, 20)}-${h.substring(20)}';
}

/// A random GUID as .NET writes it with "N" (32 hex digits).
String newGuidN() => newGuid().replaceAll('-', '');
