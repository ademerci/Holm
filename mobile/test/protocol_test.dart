// The protocol code against values made by Holm.Core (C#), and end to end over loopback.
import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:holm_mobile/app_state.dart';
import 'package:holm_mobile/net/crypto.dart';
import 'package:holm_mobile/net/friends.dart';
import 'package:holm_mobile/net/secure_channel.dart';
import 'package:holm_mobile/net/transfer.dart';

/// A friend code exported by Holm's FriendIdentity (desktop).
const desktopCode =
    'WNF1.eyJuIjoiTGFwdG9wIiwidCI6InduZi13eW9memEzMmR6bGFoZmpiZzJkZHRzbm01dyIsImsiOiJNRmt3RXdZSEtvWkl6ajBDQVFZSUtvWkl6ajBEQVFjRFFnQUVKMzFaUDZ6MEtrMFgyaWJUTGxFM1ltWDlmVWRHM3lpanF2R3llTXlUUjAtVjJzeDJTR29aeHluM0VUdzlfX1IwdVRRYm1GejhtcWVNLVkxMHI1MUlHUSIsInMiOiJodHRwczovL3JlbGF5LmhvbG1ub3RjaC5jaCJ9';

void main() {
  test('reads a friend code made by Holm on the desktop', () {
    final f = parseFriendCode('Add me: $desktopCode thanks')!;
    expect(f.name, 'Laptop');
    expect(f.topic, 'wnf-wyofza32dzlahfjbg2ddtsnm5w');
    expect(f.server, relayServer);
  });

  test('invite topic and proof match FriendInvite.cs', () {
    expect(Invite.topic('HOLM-7KQ2X-9XF4M'), 'holm-inv-qcnze7nfkvswry7f7e5d');
    expect(Invite.proof('HOLM-7KQ2X-9XF4M'), '8712e671199feb7ee20ad6dd346d5ef8');
    expect(Invite.find('holm 7kq2x 9xf4m'), 'HOLM-7KQ2X-9XF4M');
    expect(Invite.find('7KQ2X-9XF4O'), 'HOLM-7KQ2X-9XF40');
    expect(Invite.find('hello'), isNull);
  });

  test('a note sealed for a friend opens only for them', () {
    final a = Identity.create('Alex'), b = Identity.create('Lena'), c = Identity.create('Mallory');
    final toB = parseFriendCode(b.friendCode)!;
    final note = Notes.seal(a, toB, 'chat', {'M': Notes.chatText('Hej 👋'), 'I': newGuidN()});
    final opened = Notes.open(b, note)!;
    expect(opened.$1.kind, 'chat');
    expect(opened.$1.text, 'Hej 👋');
    expect(opened.$1.from, 'Alex');
    expect(opened.$2, a.publicKey);
    expect(Notes.open(c, note), isNull);
  });

  test('identity keeps its key across save and load', () {
    final me = Identity.create('Pixel');
    final back = Identity.fromJson(jsonDecode(jsonEncode(me.toJson())) as Map<String, dynamic>);
    expect(back.publicKey, me.publicKey);
    expect(back.friendCode, me.friendCode);
  });

  test('long messages are split under the note size', () {
    final parts = AppState.splitMessage(List.filled(800, 'wörd').join(' '));
    expect(parts.length, greaterThan(1));
    for (final p in parts) {
      expect(utf8.encode(p).length, lessThanOrEqualTo(AppState.maxChatBytes));
    }
  });

  test('file names are made safe as on the desktop', () {
    expect(safeFileName('../../etc/passwd'), 'passwd');
    expect(safeFileName('a:b*c?.txt'), 'a_b_c_.txt');
    expect(safeFileName('photo${String.fromCharCode(0x202E)}gpj.exe'), 'photo_gpj.exe');
    expect(safeFileName('  '), 'file');
  });

  test('files and texts travel over an encrypted channel', () async {
    final dir = await Directory.systemTemp.createTemp('holm-test');
    final server = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final results = StreamController<Received?>();
    server.listen((socket) async {
      final channel = await SecureChannel.open(socket, initiator: false);
      results.add(await receiveTransfer(channel, (_) async => true, dir.path));
      await channel.close();
    });
    final events = StreamIterator(results.stream);

    final file = File('${dir.path}/source.bin')..writeAsBytesSync(List.generate(200000, (i) => i & 0xff));
    var ch = await SecureChannel.connect(InternetAddress.loopbackIPv4, server.port);
    expect(await sendTransfer(ch, 'Phone', [Outgoing.file(file.path)]), isTrue);
    await ch.close();
    expect(await events.moveNext(), isTrue);
    final got = events.current!;
    expect(got.offer.sender, 'Phone');
    expect(File(got.paths.single).readAsBytesSync(), file.readAsBytesSync());

    ch = await SecureChannel.connect(InternetAddress.loopbackIPv4, server.port);
    expect(await sendTransfer(ch, 'Phone', [Outgoing.memory('Text.txt', utf8.encode('Grüezi ✓'))], text: true), isTrue);
    await ch.close();
    expect(await events.moveNext(), isTrue);
    expect(events.current!.text, 'Grüezi ✓');
    expect(events.current!.offer.isText, isTrue);

    await server.close();
    await dir.delete(recursive: true);
  });
}
