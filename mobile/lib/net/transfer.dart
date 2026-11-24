// Holm.Core/Sharing/FileTransfer.cs in Dart: offer (names and sizes) → answer → data frames → done → received.

import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:path/path.dart' as p;

import 'secure_channel.dart';

const _offer = 1, _answer = 2, _data = 3, _done = 4, _received = 5, _cancelled = 6;
const _chunk = 64 * 1024;
const maxFiles = 1000;
const maxTotalBytes = 100 * 1024 * 1024 * 1024;
const maxTextBytes = 1024 * 1024;

class ShareFile {
  final String name;
  final int size;
  const ShareFile(this.name, this.size);
}

class ShareOffer {
  final String sender;
  final List<ShareFile> files;
  final bool isText;
  const ShareOffer(this.sender, this.files, this.isText);
  int get totalBytes => files.fold(0, (s, f) => s + f.size);
}

/// Something to send: a file on disk, or bytes in memory (a beamed text).
class Outgoing {
  final String name;
  final String? path;
  final Uint8List? bytes;
  Outgoing.file(this.path) : name = p.basename(path!), bytes = null;
  Outgoing.memory(this.name, this.bytes) : path = null;
  int get size => bytes?.length ?? File(path!).lengthSync();
}

class SenderCancelled implements Exception {
  @override
  String toString() => 'The sender cancelled the transfer.';
}

String _trim(String? text, int max) {
  final t = (text ?? '').runes.where((r) => r >= 0x20 && r != 0x7f).map(String.fromCharCode).join().trim();
  return t.length > max ? t.substring(0, max) : t;
}

/// The same rules as FileTransfer.SafeFileName: a plain name that can only land in the target folder.
String safeFileName(String? name) {
  var n = (name ?? '').replaceAll('\\', '/');
  n = n.substring(n.lastIndexOf('/') + 1);
  const invalid = '"<>|:*?\\/';
  n = n.runes.map((r) {
    final c = String.fromCharCode(r);
    final direction = r == 0x200e || r == 0x200f || r == 0x061c || (r >= 0x202a && r <= 0x202e) || (r >= 0x2066 && r <= 0x2069);
    return r < 0x20 || r == 0x7f || direction || invalid.contains(c) ? '_' : c;
  }).join();
  n = n.trim().replaceAll(RegExp(r'[. ]+$'), '');
  if (n.isEmpty || RegExp(r'^\.+$').hasMatch(n)) n = 'file';
  if (n.length > 150) {
    var ext = p.extension(n);
    if (ext.length > 20) ext = '';
    n = n.substring(0, 150 - ext.length) + ext;
  }
  return n;
}

String uniquePath(String folder, String name) {
  var path = p.join(folder, name);
  if (!File(path).existsSync() && !Directory(path).existsSync()) return path;
  final stem = p.basenameWithoutExtension(name), ext = p.extension(name);
  for (var i = 1;; i++) {
    path = p.join(folder, '$stem ($i)$ext');
    if (!File(path).existsSync() && !Directory(path).existsSync()) return path;
  }
}

/// Offers the items and sends them if the receiver accepts. True when accepted and confirmed.
Future<bool> sendTransfer(SecureChannel channel, String sender, List<Outgoing> items,
    {bool text = false, void Function(double)? progress}) async {
  if (items.isEmpty || items.length > maxFiles) throw ArgumentError('Send between 1 and 1000 files.');
  final sizes = [for (final i in items) i.size];
  final total = sizes.fold<int>(0, (a, b) => a + b);
  if (total > maxTotalBytes) throw const FileSystemException("That's more than 100 GB.");
  final offer = <String, Object>{
    'Sender': _trim(sender, 64),
    'Files': [for (var i = 0; i < items.length; i++) {'Name': items[i].name, 'Size': sizes[i]}],
    if (text) 'Text': true,
  };
  await channel.send([_offer, ...utf8.encode(jsonEncode(offer))]);

  final answer = await channel.receive();
  if (answer.length != 2 || answer[0] != _answer) throw const FormatException('Unexpected answer.');
  if (answer[1] != 1) return false;

  var sent = 0;
  progress?.call(0);
  for (var i = 0; i < items.length; i++) {
    final item = items[i];
    if (item.bytes != null) {
      for (var at = 0; at < item.bytes!.length; at += _chunk) {
        final end = (at + _chunk).clamp(0, item.bytes!.length);
        await channel.send([_data, ...item.bytes!.sublist(at, end)]);
        sent += end - at;
        progress?.call(total == 0 ? 1 : sent / total);
      }
      continue;
    }
    var remaining = sizes[i];
    final file = await File(item.path!).open();
    try {
      final buffer = Uint8List(1 + _chunk)..[0] = _data;
      while (remaining > 0) {
        final read = await file.readInto(buffer, 1, 1 + (remaining < _chunk ? remaining : _chunk));
        if (read == 0) throw FileSystemException('${item.name} got shorter while sending.');
        await channel.send(Uint8List.sublistView(buffer, 0, 1 + read));
        remaining -= read;
        sent += read;
        progress?.call(total == 0 ? 1 : sent / total);
      }
    } finally {
      await file.close();
    }
  }
  await channel.send([_done]);
  final confirmation = await channel.receive();
  if (confirmation.length != 1 || confirmation[0] != _received) throw const FormatException("The receiver didn't confirm.");
  progress?.call(1);
  return true;
}

ShareOffer _parseOffer(List<int> json) {
  final Object? dto;
  try {
    dto = jsonDecode(utf8.decode(json));
  } catch (_) {
    throw const FormatException('Unreadable offer.');
  }
  if (dto is! Map || dto['Files'] is! List) throw const FormatException('Bad file list.');
  final list = dto['Files'] as List;
  if (list.isEmpty || list.length > maxFiles) throw const FormatException('Bad file list.');
  final files = <ShareFile>[];
  var total = 0;
  for (final f in list) {
    if (f is! Map || f['Size'] is! int || (f['Size'] as int) < 0) throw const FormatException('Bad file size.');
    total += f['Size'] as int;
    if (total > maxTotalBytes) throw const FormatException('Offer too large.');
    files.add(ShareFile(safeFileName(f['Name'] as String?), f['Size'] as int));
  }
  final sender = _trim(dto['Sender'] as String?, 64);
  final text = dto['Text'] == true && files.length == 1 && total <= maxTextBytes;
  return ShareOffer(sender.isEmpty ? 'Someone' : sender, files, text);
}

/// What arrived: the saved paths, or the text of a beamed text.
class Received {
  final ShareOffer offer;
  final List<String> paths;
  final String? text;
  Received(this.offer, this.paths, this.text);
}

/// Reads the offer, asks [decide], and on a yes saves the files into [folder] (a text stays in memory).
/// Null when declined.
Future<Received?> receiveTransfer(SecureChannel channel, Future<bool> Function(ShareOffer) decide, String folder,
    {void Function(ShareOffer, double)? progress}) async {
  final first = await channel.receive();
  if (first.length < 2 || first[0] != _offer) throw const FormatException('Expected an offer.');
  final offer = _parseOffer(first.sublist(1));

  final accept = await decide(offer);
  await channel.send([_answer, accept ? 1 : 0]);
  if (!accept) return null;

  final saved = <String>[];
  final total = offer.totalBytes;
  var received = 0;
  String? current;
  final textBytes = BytesBuilder(copy: false);
  progress?.call(offer, 0);
  try {
    if (!offer.isText) await Directory(folder).create(recursive: true);
    for (final file in offer.files) {
      var remaining = file.size;
      IOSink? out;
      if (!offer.isText) {
        current = uniquePath(folder, file.name);
        out = File(current).openWrite();
      }
      try {
        while (remaining > 0) {
          final frame = await channel.receive();
          if (frame.length == 1 && frame[0] == _cancelled) throw SenderCancelled();
          if (frame.length < 2 || frame[0] != _data) throw const FormatException('Expected file data.');
          final length = frame.length - 1;
          if (length > remaining) throw const FormatException('More data than offered.');
          final chunk = Uint8List.sublistView(frame, 1);
          if (out != null) {
            out.add(chunk);
          } else {
            textBytes.add(chunk);
          }
          remaining -= length;
          received += length;
          progress?.call(offer, total == 0 ? 1 : received / total);
        }
      } finally {
        await out?.close();
      }
      if (current != null) saved.add(current);
      current = null;
    }
    final done = await channel.receive();
    if (done.length == 1 && done[0] == _cancelled) throw SenderCancelled();
    if (done.length != 1 || done[0] != _done) throw const FormatException('Expected the end of the transfer.');
    await channel.send([_received]);
    progress?.call(offer, 1);
    return Received(offer, saved, offer.isText ? utf8.decode(textBytes.takeBytes(), allowMalformed: true) : null);
  } catch (_) {
    if (current != null) {
      try {
        File(current).deleteSync();
      } catch (_) {}
    }
    rethrow;
  }
}
