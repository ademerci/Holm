// Holm.Core/Sharing/SecureChannel.cs and NearbyService.AgreeKeyAsync in Dart: an ephemeral ECDH handshake, then
// length-prefixed AES-256-GCM frames with a per-direction nonce counter.

import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'crypto.dart';

/// Reads exact byte counts from a socket.
class ByteReader {
  final StreamIterator<Uint8List> _it;
  Uint8List _buf = Uint8List(0);
  int _pos = 0;

  ByteReader(Stream<Uint8List> stream) : _it = StreamIterator(stream);

  Future<Uint8List> read(int n) async {
    if (n == 0) return Uint8List(0);
    final out = BytesBuilder(copy: false);
    var need = n;
    while (need > 0) {
      if (_pos >= _buf.length) {
        if (!await _it.moveNext()) throw const SocketException('The other side hung up.');
        _buf = _it.current;
        _pos = 0;
        continue;
      }
      final take = min(need, _buf.length - _pos);
      out.add(Uint8List.sublistView(_buf, _pos, _pos + take));
      _pos += take;
      need -= take;
    }
    return out.takeBytes();
  }

  Future<void> cancel() => _it.cancel();
}

class SecureChannel {
  static const maxPlaintext = 1 << 20;
  static const _tag = 16;

  final Socket socket;
  final ByteReader _reader;
  final Uint8List _key;
  final int _sendDirection, _receiveDirection;
  int _sent = 0, _received = 0;
  Future<void> _writes = Future.value();

  SecureChannel._(this.socket, this._reader, this._key, bool initiator)
      : _sendDirection = initiator ? 1 : 2,
        _receiveDirection = initiator ? 2 : 1;

  String get remoteAddress => socket.remoteAddress.address;

  /// Connects (the sender's side) and agrees on a key.
  static Future<SecureChannel> connect(InternetAddress address, int port) async {
    final socket = await Socket.connect(address, port, timeout: const Duration(seconds: 8));
    socket.setOption(SocketOption.tcpNoDelay, true);
    try {
      return await open(socket, initiator: true).timeout(const Duration(seconds: 8));
    } catch (_) {
      socket.destroy();
      rethrow;
    }
  }

  /// A channel with a key both sides already have (ShareCode.Key, over the relay), on a socket whose
  /// [reader] may already have read the relay's answer.
  static SecureChannel withKey(Socket socket, ByteReader reader, Uint8List key, {required bool initiator}) =>
      SecureChannel._(socket, reader, key, initiator);

  /// Agrees on a key over an accepted or connected socket.
  static Future<SecureChannel> open(Socket socket, {required bool initiator}) async {
    final reader = ByteReader(socket);
    final key = await agreeKey(socket, reader, initiator);
    return SecureChannel._(socket, reader, key, initiator);
  }

  static Future<Uint8List> agreeKey(Socket socket, ByteReader reader, bool initiator) async {
    final ecdh = EcKey.generate();
    final mine = ecdh.spki();
    socket.add([mine.length >> 8, mine.length & 0xff]);
    socket.add(mine);
    await socket.flush();

    final header = await reader.read(2);
    final length = (header[0] << 8) | header[1];
    if (length < 32 || length > 512) throw const FormatException('Bad key.');
    final theirs = await reader.read(length);
    final other = parseSpki(theirs);

    final transcript = sha256(initiator ? concat([mine, theirs]) : concat([theirs, mine]));
    return ecdh.deriveKeyFromHash(other, ascii.encode('WinNotch LAN v1'), transcript);
  }

  static Uint8List _nonce(int direction, int counter) {
    final n = ByteData(12);
    n.setUint32(0, direction);
    n.setUint64(4, counter);
    return n.buffer.asUint8List();
  }

  Future<void> send(List<int> plaintext) {
    if (plaintext.length > maxPlaintext) throw ArgumentError('Frame too large.');
    final sealed = aesGcm(true, _key, _nonce(_sendDirection, _sent++), Uint8List.fromList(plaintext));
    final frame = Uint8List(4 + sealed.length);
    ByteData.sublistView(frame).setInt32(0, sealed.length);
    frame.setRange(4, frame.length, sealed);
    _writes = _writes.then((_) async {
      socket.add(frame);
      await socket.flush();
    });
    return _writes;
  }

  Future<Uint8List> receive() async {
    final header = await _reader.read(4);
    final length = ByteData.sublistView(header).getInt32(0);
    if (length < _tag || length > maxPlaintext + _tag) throw const FormatException('Bad frame length.');
    final data = await _reader.read(length);
    return aesGcm(false, _key, _nonce(_receiveDirection, _received++), data);
  }

  Future<void> close() async {
    try {
      await _writes.timeout(const Duration(seconds: 2));
    } catch (_) {}
    socket.destroy();
    unawaited(_reader.cancel().catchError((_) {}));
  }
}
