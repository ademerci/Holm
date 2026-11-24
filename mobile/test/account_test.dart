// The account and relay code against values made by Holm.Core (C#): AccountKeys.Derive, AccountVault, an identity
// section as Holm on the PC seals it, and ShareCode's key and relay token.
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:holm_mobile/net/account.dart';
import 'package:holm_mobile/net/crypto.dart';
import 'package:holm_mobile/net/relay.dart';
import 'package:pointycastle/export.dart';

const email = ' Elias@Example.CH ';
const password = 'pässwort'; // "ä" as a + combining dots: must make the same keys as "ä"
const signIn = 'oF8jpgWSDBIYDjpTkoS1lA6PPJVImmFfsUKliDbFHlU';
const vault = 'c10434dfcfd0b7ceb22e6fc68133235df2be6feee626ddfdffa6a117f713f417';
const wrapped = 'v1.qSr4wDs0peaCbRVipXyeuXzou-CjVr2LnqN94jCt3PGJsHkhlKp_tigBXr5BDYwakTAVNU3saYrVNODz';
const identity =
    'v1.NfJc8TZ5ttGPwnZ3ADwP1e-VGkjcctb3q6kp6hAqAjoU2Sl0PvB4R0zmRhLVWVRjE2cZSD6VNnOMfldt3OGk2tTPhnYRHtmAq2AVZ7biOIzR8sl_nkEmy9xIwPzK6CezRd7z6Fh7WqMiTFVs9o1xjUWCLLq8XrjSEbmx00n_QNkB74O5rHNGZNqJ52zJSLP2X-_kyc07dqJ6d4i7iuKze20X6zV1yZhf6Yg2mChLin_J1Rshsq1SAArjZDfOHOiyXyB4BNry9y329yIamsAQot6iCxY7SGiIuL9MEjA4UlR_mOLG9EYdbp0DBVLjS_oIZWIwFZkY9Iedykca8JUaV3DMaghofPSpFLm9R4qZQBxNhh3_ZeENVIIWeBGx1zdgB0qoEidGu_Iki54kNntXBtskS9cPlpI';
const identityPublic =
    'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEJbz2Eaw18Ks4p4semb0a5YBV9roCa1jSlKZnN1j09PBDXz8uacXFTuoDPB-aIabWJPLGb0VWQg-3coQ2aPFFAQ';

void main() {
  test('PBKDF2 matches the general implementation', () {
    final mac = PBKDF2KeyDerivator(HMac(SHA256Digest(), 64))..init(Pbkdf2Parameters(Uint8List.fromList(utf8.encode('salt')), 1000, 32));
    expect(toHex(pbkdf2Sha256(utf8.encode('password'), utf8.encode('salt'), 1000, 32)),
        toHex(mac.process(Uint8List.fromList(utf8.encode('password')))));
  });

  test('keys from email and password match AccountKeys.Derive', () async {
    final watch = Stopwatch()..start();
    final keys = await AccountKeys.derive(email, password);
    // ignore: avoid_print
    print('600 000 rounds took ${watch.elapsedMilliseconds} ms (JIT; a release build is faster)');
    expect(keys.signInSecret, signIn);
    expect(toHex(keys.vaultKey), vault);
  });

  test('the vault and an identity section open as Holm on the PC sealed them', () {
    final keys = AccountKeys(signIn, Uint8List.fromList(List.generate(32, (i) => int.parse(vault.substring(i * 2, i * 2 + 2), radix: 16))));
    final dataKey = AccountVault.unwrapDataKey(keys, 'uid123', wrapped);
    expect(dataKey, List.generate(32, (i) => i));
    expect(() => AccountVault.unwrapDataKey(keys, 'someone-else', wrapped), throwsA(isA<AccountException>()));

    final payload = jsonDecode(utf8.decode(AccountVault.openSection(dataKey, 'uid123', 'identity', identity))) as Map<String, dynamic>;
    expect(payload['Topic'], 'wnf-abcdefghijklmnopqrstuvwxyz');
    final key = EcKey.fromPrivate(ecPrivateKeyFromPkcs8(base64Decode(payload['Key'] as String)));
    expect(base64Url(key.spki()), identityPublic);
    expect(() => AccountVault.openSection(dataKey, 'uid123', 'friends', identity), throwsA(anything));
  });

  test('share secrets give the key and relay channel of ShareCode.cs', () {
    final secret = List.generate(16, (i) => 200 + i);
    expect(toHex(ShareSecret.key(secret)), '484fdaa2e312f82fd91a11f1508bccd84d23b8a858760785e0d6de714160106f');
    expect(toHex(ShareSecret.relayToken(secret)), '4e640f12cfad415f89f121f5209ee840417200b3b9bd4d55e70f123dbc275773');
  });
}
