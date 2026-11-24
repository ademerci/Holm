import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

import 'net/account.dart';
import 'net/crypto.dart';
import 'net/friends.dart';
import 'net/nearby.dart';
import 'net/relay.dart';
import 'net/secure_channel.dart';
import 'net/transfer.dart';
import 'notch/link.dart';
import 'notch/settings.dart';
import 'update.dart';

enum ChatState { sending, sent, failed, received }

class ChatMessage {
  final String id;
  final bool mine;
  final String text;
  final int sent;
  ChatState state;
  String? myReaction, theirReaction;

  ChatMessage({required this.id, required this.mine, required this.text, required this.sent, required this.state,
      this.myReaction, this.theirReaction});

  Map<String, dynamic> toJson() =>
      {'id': id, 'mine': mine, 'text': text, 'sent': sent, 'state': state.name, 'my': myReaction, 'their': theirReaction};
  factory ChatMessage.fromJson(Map<String, dynamic> j) => ChatMessage(
      id: j['id'] as String,
      mine: j['mine'] as bool,
      text: j['text'] as String,
      sent: j['sent'] as int,
      state: ChatState.values.byName(j['state'] as String),
      myReaction: j['my'] as String?,
      theirReaction: j['their'] as String?);
}

class ClipItem {
  final String text;
  final DateTime time;
  final String? from; // the device it came from; null: copied here
  ClipItem(this.text, this.time, [this.from]);
  Map<String, dynamic> toJson() => {'text': text, 'time': time.millisecondsSinceEpoch, 'from': from};
  factory ClipItem.fromJson(Map<String, dynamic> j) =>
      ClipItem(j['text'] as String, DateTime.fromMillisecondsSinceEpoch(j['time'] as int), j['from'] as String?);
}

class FriendRequest {
  final String requestId;
  final Friend card;
  final int sent;
  FriendRequest(this.requestId, this.card, this.sent);
  Map<String, dynamic> toJson() => {'id': requestId, 'card': card.toJson(), 'sent': sent};
  factory FriendRequest.fromJson(Map<String, dynamic> j) =>
      FriendRequest(j['id'] as String, Friend.fromJson(j['card'] as Map<String, dynamic>), j['sent'] as int);
}

class ReceivedFile {
  final String path;
  final String from;
  final DateTime time;
  ReceivedFile(this.path, this.from, this.time);
  String get name => p.basename(path);
  Map<String, dynamic> toJson() => {'path': path, 'from': from, 'time': time.millisecondsSinceEpoch};
  factory ReceivedFile.fromJson(Map<String, dynamic> j) =>
      ReceivedFile(j['path'] as String, j['from'] as String, DateTime.fromMillisecondsSinceEpoch(j['time'] as int));
}

/// Another device of the same Holm account (a PC or a phone) that said lately that it runs. It shares our identity,
/// so it reads our inbox (Holm.Core/Sharing/OwnDevices.cs).
class OwnDevice {
  final String id;
  final String name;
  final DateTime seen;
  const OwnDevice(this.id, this.name, this.seen);

  static bool isId(Object? id) => id is String && RegExp(r'^[0-9a-f]{32}$').hasMatch(id);
}

/// Where something can be sent: a Holm on this Wi-Fi, or a device of the account anywhere.
class SendTarget {
  final NearbyPeer? peer;
  final OwnDevice? device;
  const SendTarget.nearby(NearbyPeer this.peer) : device = null;
  const SendTarget.device(OwnDevice this.device) : peer = null;

  String get name => peer?.name ?? device!.name;
  bool get overInternet => device != null;
}

/// The Holm account the phone is signed in to.
class AccountInfo {
  final String email;
  final String uid;
  final String refreshToken;
  final Uint8List dataKey;
  const AccountInfo(this.email, this.uid, this.refreshToken, this.dataKey);

  Map<String, dynamic> toJson() => {'email': email, 'uid': uid, 'refresh': refreshToken, 'key': base64Encode(dataKey)};
  static AccountInfo? fromJson(Object? j) {
    if (j is! Map<String, dynamic>) return null;
    try {
      return AccountInfo(j['email'] as String, j['uid'] as String, j['refresh'] as String, base64Decode(j['key'] as String));
    } catch (_) {
      return null;
    }
  }
}

/// What a transfer is doing, for the banner on the Devices page.
class TransferStatus {
  final String title;
  final String detail;
  final double? progress;
  final bool failed;
  final bool done;
  const TransferStatus(this.title, this.detail, {this.progress, this.failed = false, this.done = false});
}

/// Everything the app knows and does. The screens only read it and call its methods.
class AppState extends ChangeNotifier {
  static const keepMessages = 300;
  static const maxChatBytes = 1500;

  late final File _file;
  late final String receivedFolder;
  late Identity me;
  late final NearbyService nearby;

  String deviceName = 'Holm phone';
  /// This phone among the account's devices: made once, so its own notes are skipped when they come back.
  String deviceId = newGuidN();
  AccountInfo? account;
  /// The Dynamic Notch's settings (lib/notch), kept here and handed to the notch.
  NotchSettings notch = const NotchSettings();
  /// The app is open in front; otherwise Holm's events and questions go to the notch.
  bool foreground = true;
  bool accountBusy = false;
  String? accountError;
  final Map<String, OwnDevice> _devices = {};
  DateTime? _announced;
  bool clipboardSync = false; // send what's copied here when the app opens, copy texts that arrive
  String? syncTarget; // the device name clipboard sync goes to (Holm desktop gets a new id each start)
  /// The device something was last sent to (by name): where a screenshot from the notch goes.
  String? lastTarget;
  String _devicesTold = '';
  final List<String> addresses = []; // laptops added by address, asked again at every start

  final List<Friend> friends = [];
  final Map<String, List<ChatMessage>> chats = {};
  final Map<String, int> unread = {};
  final Map<String, DateTime> lastSeen = {};
  final List<FriendRequest> outgoing = [];
  final List<FriendRequest> incoming = [];
  final List<ClipItem> clipboard = [];
  /// The note, as Holm's notepad on the desktop: one text, at most [maxNote] characters.
  String note = '';
  static const maxNote = 4000;
  final List<ReceivedFile> received = [];
  final List<String> _seen = [];
  int? _since;

  TransferStatus? transfer;
  String? inboxError;
  String? _lastClip;

  /// Asks the person whether to take an offer (set by the UI).
  Future<bool> Function(ShareOffer offer)? askOffer;

  /// A short message for the person (set by the UI).
  void Function(String message)? toast;

  WebSocket? _ws;
  Timer? _reconnect, _poll, _save;
  bool _following = false;
  final Map<String, DateTime> _pinged = {};

  Future<void> load() async {
    final support = await getApplicationSupportDirectory();
    final docs = await getApplicationDocumentsDirectory();
    _file = File(p.join(support.path, 'holm.json'));
    receivedFolder = p.join(docs.path, 'Received');
    Map<String, dynamic> j = {};
    if (await _file.exists()) {
      try {
        j = jsonDecode(await _file.readAsString()) as Map<String, dynamic>;
      } catch (_) {}
    }
    deviceName = j['deviceName'] as String? ?? deviceName;
    clipboardSync = j['clipboardSync'] as bool? ?? false;
    syncTarget = j['syncTarget'] as String?;
    lastTarget = j['lastTarget'] as String?;
    addresses.addAll([for (final x in (j['addresses'] as List? ?? [])) x as String]);
    me = j['identity'] != null ? Identity.fromJson(j['identity'] as Map<String, dynamic>) : Identity.create(deviceName);
    if (OwnDevice.isId(j['deviceId'])) deviceId = j['deviceId'] as String;
    account = AccountInfo.fromJson(j['account']);
    notch = NotchSettings.fromJsonString(j['notch'] as String?);
    NotchLink.init();
    Updater.instance.beta = notch.betaUpdates;
    Updater.instance.start();
    NotchLink.onMute = (pkg, name) => setNotch(notch.copyWith(muted: {...notch.muted, pkg: name}));
    NotchLink.onBeamFile = _beamFromNotch;
    NotchLink.onPhoneStatus = _phoneStatus;
    addListener(_tellNotchDevices);
    NotchLink.settings(notch);
    for (final f in (j['friends'] as List? ?? [])) {
      friends.add(Friend.fromJson(f as Map<String, dynamic>));
    }
    (j['chats'] as Map<String, dynamic>? ?? {}).forEach((k, v) {
      chats[k] = [for (final m in v as List) ChatMessage.fromJson(m as Map<String, dynamic>)];
    });
    (j['unread'] as Map<String, dynamic>? ?? {}).forEach((k, v) => unread[k] = v as int);
    for (final r in (j['outgoing'] as List? ?? [])) {
      outgoing.add(FriendRequest.fromJson(r as Map<String, dynamic>));
    }
    for (final r in (j['incoming'] as List? ?? [])) {
      incoming.add(FriendRequest.fromJson(r as Map<String, dynamic>));
    }
    for (final c in (j['clipboard'] as List? ?? [])) {
      clipboard.add(ClipItem.fromJson(c as Map<String, dynamic>));
    }
    note = j['note'] as String? ?? '';
    for (final r in (j['received'] as List? ?? [])) {
      final file = ReceivedFile.fromJson(r as Map<String, dynamic>);
      if (File(file.path).existsSync()) received.add(file);
    }
    _seen.addAll([for (final s in (j['seen'] as List? ?? [])) s as String]);
    _since = j['since'] as int?;
    _lastClip = j['lastClip'] as String?;

    nearby = NearbyService(deviceName)..incoming = _onIncoming;
    nearby.changes.listen((_) => notifyListeners());
    try {
      await nearby.start();
      for (final host in addresses) {
        await nearby.addAddress(host);
      }
    } catch (e) {
      toast?.call("Couldn't start nearby sharing: $e");
    }
    _saveNow();
    startInbox();
    if (account != null) unawaited(refreshAccount());
  }

  void _changed() {
    notifyListeners();
    _save?.cancel();
    _save = Timer(const Duration(milliseconds: 400), _saveNow);
  }

  void _saveNow() {
    final j = {
      'deviceName': deviceName,
      'deviceId': deviceId,
      'account': account?.toJson(),
      'notch': notch.toJsonString(),
      'clipboardSync': clipboardSync,
      'syncTarget': syncTarget,
      'lastTarget': lastTarget,
      'addresses': addresses,
      'identity': me.toJson(),
      'friends': [for (final f in friends) f.toJson()],
      'chats': {for (final e in chats.entries) e.key: [for (final m in e.value) m.toJson()]},
      'unread': unread,
      'outgoing': [for (final r in outgoing) r.toJson()],
      'incoming': [for (final r in incoming) r.toJson()],
      'clipboard': [for (final c in clipboard) c.toJson()],
      'note': note,
      'received': [for (final r in received) r.toJson()],
      'seen': _seen,
      'since': _since,
      'lastClip': _lastClip,
    };
    try {
      final temp = File('${_file.path}.tmp');
      temp.writeAsStringSync(jsonEncode(j));
      temp.renameSync(_file.path);
    } catch (_) {}
  }

  // ───────────── settings ─────────────

  void setDeviceName(String name) {
    name = name.trim();
    if (name.isEmpty || name == deviceName) return;
    deviceName = name.length > 40 ? name.substring(0, 40) : name;
    nearby.name = deviceName;
    if (account == null) me = me.withName(deviceName); // signed in, friends see the account's profile name
    nearby.refresh();
    announceDevice(force: true);
    _changed();
  }

  void setNotch(NotchSettings next) {
    if (next.betaUpdates != notch.betaUpdates) {
      Updater.instance.beta = next.betaUpdates;
      unawaited(Updater.instance.check());
    }
    notch = next;
    NotchLink.settings(next);
    _changed();
  }

  /// The app went to the background or came back: a running transfer moves to the notch, and back.
  void setForeground(bool front) {
    if (foreground == front) return;
    foreground = front;
    final t = transfer;
    if (!front && t != null && !t.done) _toNotch(t);
  }

  /// The notch's overview shows which of Holm's devices are there.
  void _tellNotchDevices() {
    final names = [for (final t in targets) t.name];
    final key = names.join('\n');
    if (key == _devicesTold) return;
    _devicesTold = key;
    NotchLink.send({'type': 'holm.devices', 'devices': names});
  }

  /// What happens on the phone, for the notch of the account's PCs: a note in our own inbox, live only (a PC that
  /// isn't running doesn't need yesterday's call). Holm.Core: FriendNoteKind.Phone.
  void _phoneStatus(Map<String, Object?> status) {
    if (account == null || !notch.toPc) return;
    final on = status['on'] != false;
    unawaited(Inbox.post(
      _ownCard,
      Notes.seal(me, _ownCard, 'phone', {
        'D': deviceId,
        'Y': status['kind'],
        'V': status['title'],
        'W': status['detail'],
        'Z': status['since'],
        'A': on ? null : false,
      }, from: deviceName),
      keep: false,
    ).catchError((_) {}));
  }

  /// "Send" on a screenshot in the notch: to the device something last went to, else the first one there.
  Future<void> _beamFromNotch(String uri) async {
    final target = _targetNamed(lastTarget) ?? (targets.isEmpty ? null : targets.first);
    if (target == null) {
      _status(TransferStatus('No PC to send to', 'Open Holm on your PC, or sign in to your Holm account', failed: true, done: true));
      return;
    }
    final path = await NotchLink.copyUri(uri);
    if (path == null) {
      _status(TransferStatus("Couldn't send", 'The screenshot could not be read', failed: true, done: true));
      return;
    }
    await sendFiles(target, [path]);
  }

  void _toNotch(TransferStatus t) => NotchLink.send({
        'type': 'holm.transfer',
        'title': t.title,
        'detail': t.detail,
        'progress': t.progress,
        'done': t.done,
        'failed': t.failed,
      });

  void setClipboardSync(bool on, [String? target]) {
    clipboardSync = on;
    if (target != null) syncTarget = target;
    _changed();
    if (on) syncClipboardNow();
  }

  /// Asks one address directly and remembers it (a network that drops broadcasts).
  Future<void> addAddress(String host) async {
    await nearby.addAddress(host);
    final clean = host.trim();
    if (!addresses.contains(clean)) addresses.add(clean);
    _changed();
  }

  // ───────────── nearby: sending ─────────────

  /// Everywhere something can go: Holms on this Wi-Fi first (faster), then the account's devices elsewhere. A device
  /// that is both on this Wi-Fi and in the account shows once, as nearby.
  List<SendTarget> get targets {
    final list = [for (final peer in nearby.peers) SendTarget.nearby(peer)];
    final names = {for (final peer in nearby.peers) peer.name};
    for (final device in ownDevices) {
      if (!names.contains(device.name)) list.add(SendTarget.device(device));
    }
    return list;
  }

  SendTarget? _targetNamed(String? name) {
    for (final t in targets) {
      if (t.name == name) return t;
    }
    return null;
  }

  Future<void> sendFiles(SendTarget target, List<String> paths) =>
      _send(target, [for (final path in paths) Outgoing.file(path)], text: false);

  Future<bool> sendText(SendTarget target, String text, {bool quiet = false}) {
    final bytes = Uint8List.fromList(utf8.encode(text));
    if (bytes.length > maxTextBytes) {
      return _send(target, [Outgoing.memory('Text.txt', bytes)], text: false, quiet: quiet);
    }
    return _send(target, [Outgoing.memory('Text.txt', bytes)], text: true, quiet: quiet);
  }

  Future<bool> _send(SendTarget target, List<Outgoing> items, {required bool text, bool quiet = false}) async {
    final what = text ? 'text' : (items.length == 1 ? items.first.name : '${items.length} files');
    final peerName = target.name;
    final waiting = TransferStatus('Waiting for $peerName', text ? 'They choose whether to copy it' : 'They choose whether to accept $what');
    SecureChannel? channel;
    try {
      if (target.device case final device?) {
        // Anywhere: a note in our inbox for that device, then both meet on the relay (as Holm sends to friends).
        _status(TransferStatus('Asking $peerName…', 'Over the internet'));
        final secret = ShareSecret.create();
        await Inbox.post(_ownCard,
            Notes.seal(me, _ownCard, 'files', {'S': base64Encode(secret), 'D': deviceId, 'N': device.id}, from: deviceName));
        _status(waiting);
        channel = await RelayLink.connectWithRetry(secret, initiator: true, patience: const Duration(seconds: 40));
      } else {
        _status(waiting);
        channel = await NearbyService.connect(target.peer!);
      }
      final accepted = await sendTransfer(channel, deviceName, items, text: text, progress: (v) {
        _status(TransferStatus('Sending to $peerName', what, progress: v));
      });
      if (accepted && lastTarget != peerName) {
        lastTarget = peerName;
        _changed();
      }
      _status(accepted
          ? TransferStatus(text ? 'Text sent' : 'Sent', 'To $peerName · $what', done: true)
          : TransferStatus('Declined', '$peerName said no', failed: true, done: true));
      return accepted;
    } catch (e) {
      final device = target.device;
      if (device != null && e is TimeoutException) {
        _devices.remove(device.id); // it's gone, whatever it said a minute ago
        _status(TransferStatus("$peerName isn't available", "Holm isn't open there right now", failed: true, done: true));
      } else {
        _status(TransferStatus("Couldn't send", _why(e), failed: true, done: true));
      }
      return false;
    } finally {
      await channel?.close();
    }
  }

  String _why(Object e) {
    if (e is SocketException || e is TimeoutException) return 'The other device could not be reached. Still on the same Wi-Fi?';
    if (e is SenderCancelled) return 'The sender cancelled';
    return e.toString();
  }

  Timer? _clearStatus;
  void _status(TransferStatus s) {
    transfer = s;
    if (!foreground) _toNotch(s);
    _clearStatus?.cancel();
    if (s.done) {
      _clearStatus = Timer(const Duration(seconds: 6), () {
        transfer = null;
        notifyListeners();
      });
    }
    notifyListeners();
  }

  void dismissTransfer() {
    transfer = null;
    notifyListeners();
  }

  // ───────────── nearby: receiving ─────────────

  Future<void> _onIncoming(SecureChannel channel, {String? knownSender}) async {
    String sender = knownSender ?? '';
    try {
      final got = await receiveTransfer(channel, (offer) async {
        // Over the relay the note proved which device it is: show the name we know it by.
        if (knownSender != null) offer = ShareOffer(knownSender, offer.files, offer.isText);
        sender = offer.sender;
        if (offer.isText && clipboardSync) return true; // sync is on: texts go straight to the clipboard
        final ask = askOffer;
        if (ask == null || !foreground) {
          // The app isn't open: the notch asks.
          final summary = offer.files.length == 1 ? offer.files.first.name : '${offer.files.length} files';
          return NotchLink.ask(offer.sender, summary, offer.isText);
        }
        return ask(offer).timeout(const Duration(seconds: 60), onTimeout: () => false);
      }, receivedFolder, progress: (offer, v) {
        if (!offer.isText) _status(TransferStatus('Receiving from ${offer.sender}', '${offer.files.length} file(s)', progress: v));
      });
      if (got == null) {
        _status(TransferStatus('Declined', 'Nothing was saved from $sender', done: true, failed: true));
        return;
      }
      if (got.text != null) {
        await Clipboard.setData(ClipboardData(text: got.text!));
        _lastClip = got.text;
        _addClip(ClipItem(got.text!, DateTime.now(), sender));
        if (foreground) {
          _status(TransferStatus('Text from $sender', 'Copied to your clipboard', done: true));
        } else {
          // In the notch: what was copied, and a button to copy it again.
          NotchLink.send({'type': 'holm.clipboard', 'text': got.text, 'from': sender});
        }
        return;
      }
      for (final path in got.paths) {
        received.insert(0, ReceivedFile(path, sender, DateTime.now()));
      }
      _status(TransferStatus(
          got.paths.length == 1 ? 'Saved ${p.basename(got.paths.first)}' : 'Saved ${got.paths.length} files',
          'From $sender',
          done: true));
      _changed();
    } catch (e) {
      _status(TransferStatus("Couldn't receive", _why(e), failed: true, done: true));
    }
  }

  void deleteReceived(ReceivedFile file) {
    received.remove(file);
    try {
      File(file.path).deleteSync();
    } catch (_) {}
    _changed();
  }

  // ───────────── clipboard ─────────────

  void _addClip(ClipItem item) {
    clipboard.removeWhere((c) => c.text == item.text);
    clipboard.insert(0, item);
    if (clipboard.length > 50) clipboard.removeRange(50, clipboard.length);
    _changed();
  }

  void removeClip(ClipItem item) {
    clipboard.remove(item);
    _changed();
  }

  void clearClipboardHistory() {
    clipboard.clear();
    _changed();
  }

  /// Reads the clipboard into the history; with sync on, sends it to the sync device when it's new.
  /// Android lets an app read the clipboard only while it is in front, so this runs when the app opens.
  Future<void> syncClipboardNow() async {
    final data = await Clipboard.getData(Clipboard.kTextPlain);
    final text = data?.text;
    if (text == null || text.trim().isEmpty) return;
    final isNew = text != _lastClip;
    _lastClip = text;
    _addClip(ClipItem(text, DateTime.now()));
    if (!isNew || !clipboardSync) return;
    final target = _targetNamed(syncTarget);
    if (target != null) await sendText(target, text, quiet: true);
  }

  Future<void> copy(String text) async {
    await Clipboard.setData(ClipboardData(text: text));
    _lastClip = text;
    _addClip(ClipItem(text, DateTime.now()));
  }

  // ───────────── note ─────────────

  void setNote(String text) {
    if (text.length > maxNote) text = text.substring(0, maxNote);
    if (text == note) return;
    note = text;
    _changed();
  }

  // ───────────── the Holm account and its devices ─────────────

  /// Our own inbox as a card: notes to the account's other devices are sealed for it and posted to it.
  Friend get _ownCard => Friend(name: deviceName, topic: me.topic, key: me.publicKey, server: relayServer);

  /// The account's other devices that spoke within ten minutes.
  List<OwnDevice> get ownDevices {
    final now = DateTime.now();
    return _devices.values.where((d) => now.difference(d.seen) < const Duration(minutes: 10)).toList()
      ..sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
  }

  /// Signs in, opens the vault and takes the account's identity and friends. Null when it worked, else why not.
  Future<String?> signIn(String email, String password) async {
    accountBusy = true;
    accountError = null;
    notifyListeners();
    try {
      final keys = await AccountKeys.derive(email, password);
      final session = await Firebase.signIn(AccountKeys.normalizeEmail(email), keys.signInSecret);
      final wrapped = await Firebase.field('users/${session.uid}', 'vault', session.idToken);
      if (wrapped == null) return 'This account has no data yet. Sign in to it in Holm on your PC first.';
      final dataKey = AccountVault.unwrapDataKey(keys, session.uid, wrapped);
      final data = await AccountData.fetch(session.uid, session.idToken, dataKey);
      if (!data.hasIdentity) {
        return "Holm on your PC hasn't put your friends key into the account yet. Open Holm there, check that you're signed in, and try again.";
      }
      account = AccountInfo(AccountKeys.normalizeEmail(email), session.uid, session.refreshToken, dataKey);
      _apply(data);
      return null;
    } on AccountException catch (e) {
      return e.message;
    } catch (e) {
      return "Couldn't sign in: $e";
    } finally {
      accountBusy = false;
      _changed();
    }
  }

  /// Takes the account's newest identity, friends and name (at every start: another PC may have changed them).
  Future<void> refreshAccount() async {
    final info = account;
    if (info == null) return;
    try {
      final session = await Firebase.refresh(info.refreshToken);
      account = AccountInfo(info.email, info.uid, session.refreshToken, info.dataKey);
      _apply(await AccountData.fetch(info.uid, session.idToken, info.dataKey));
      accountError = null;
    } on AccountException catch (e) {
      if (e.code != null) accountError = e.message;
    } catch (_) {}
    _changed();
  }

  void _apply(AccountData data) {
    final name = (data.profileName?.isNotEmpty ?? false) ? data.profileName! : deviceName;
    if (data.hasIdentity) {
      final key = EcKey.fromPrivate(data.key!);
      if (data.topic != me.topic || base64Url(key.spki()) != me.publicKey) {
        // Another inbox: read it from now on, as Holm does when it takes the account's identity.
        me = Identity(name, data.topic!, key);
        _since = unixNow();
        _seen.clear();
        incoming.clear();
        stopInbox();
        startInbox();
      } else if (me.name != name) {
        me = me.withName(name);
      }
    }
    for (final f in data.friends) {
      final key = f['Key'], topic = f['Topic'];
      if (key is! String || topic is! String || data.removed.contains(key) || key == me.publicKey) continue;
      final existing = friendByKey(key);
      if (existing != null) {
        if (f['Name'] is String) existing.name = f['Name'] as String;
        continue;
      }
      final code = base64Url(utf8.encode(jsonEncode({'n': f['Name'], 't': topic, 'k': key, 's': f['Server']})));
      final card = parseFriendCode('WNF1.$code');
      if (card != null) friends.add(card);
    }
    friends.removeWhere((f) => data.removed.contains(f.key));
    announceDevice(force: true);
  }

  /// Signs out: the account's key leaves the phone, and so do the friends and chats that came with it.
  void signOut() {
    account = null;
    accountError = null;
    _devices.clear();
    me = Identity.create(deviceName);
    friends.clear();
    chats.clear();
    unread.clear();
    incoming.clear();
    outgoing.clear();
    _since = unixNow();
    _seen.clear();
    stopInbox();
    startInbox();
    _changed();
  }

  /// Says in our inbox that this phone runs and, with [query], asks the account's other devices to say so too.
  void announceDevice({bool force = false, bool query = true}) {
    if (account == null) return;
    final last = _announced;
    if (!force && query && last != null && DateTime.now().difference(last) < const Duration(seconds: 30)) return;
    if (query) _announced = DateTime.now();
    Inbox.post(_ownCard, Notes.seal(me, _ownCard, 'device', {'D': deviceId, 'H': query ? true : null}, from: deviceName),
            keep: false)
        .catchError((_) {});
  }

  void _handleOwn(Note note, int age) {
    final id = note.fields['D'];
    if (account == null || !OwnDevice.isId(id) || id == deviceId) return; // ours, back from the inbox
    final from = note.from.isEmpty ? 'Holm' : note.from;
    switch (note.kind) {
      case 'device' when age <= 30:
        _devices[id as String] = OwnDevice(id, from, DateTime.now());
        if (note.fields['H'] == true) announceDevice(query: false);
      case 'files' when note.fields['N'] == deviceId && age <= 60:
        final secret = note.fields['S'];
        if (secret is! String) return;
        final bytes = base64Decode(secret);
        if (bytes.length != ShareSecret.length) return;
        final name = _devices[id]?.name ?? from;
        _devices[id as String] = OwnDevice(id, name, DateTime.now());
        unawaited(_receiveOverRelay(bytes, name));
      default:
        break;
    }
  }

  Future<void> _receiveOverRelay(List<int> secret, String from) async {
    _status(TransferStatus('$from is sending you something', 'Connecting…'));
    try {
      final channel = await RelayLink.connectWithRetry(secret, initiator: false, patience: const Duration(minutes: 2));
      try {
        await _onIncoming(channel, knownSender: from);
      } finally {
        await channel.close();
      }
    } catch (e) {
      _status(TransferStatus("Couldn't receive", e is TimeoutException ? '$from cancelled or went offline' : _why(e),
          failed: true, done: true));
    }
  }

  // ───────────── friends and chat ─────────────

  Friend? friendByKey(String key) {
    for (final f in friends) {
      if (f.key == key) return f;
    }
    return null;
  }

  bool isOnline(Friend f) {
    final seen = lastSeen[f.key];
    return seen != null && DateTime.now().difference(seen) < const Duration(minutes: 3);
  }

  List<ChatMessage> chatWith(Friend f) => chats[f.key] ?? const [];
  int get unreadTotal => unread.values.fold(0, (a, b) => a + b);

  void markRead(Friend f) {
    if (unread.remove(f.key) != null) _changed();
  }

  /// Adds a friend from a long friend code or a short invite code: sends them a friend request.
  Future<String> addFriend(String text) async {
    final invite = parseFriendCode(text) == null ? Invite.find(text) : null;
    Friend? card;
    String? proof;
    if (invite != null) {
      card = await Inbox.fetchInvite(invite);
      if (card == null) return 'That invite code has expired or is mistyped';
      proof = Invite.proof(invite);
    } else {
      card = parseFriendCode(text);
    }
    if (card == null) return 'Paste a Holm friend code (WNF1.…) or an invite code (HOLM-XXXXX-XXXXX)';
    if (card.key == me.publicKey) return "That's your own code";
    if (friendByKey(card.key) != null) return '${card.name} is already your friend';
    final requestId = newGuidN();
    outgoing.removeWhere((r) => r.card.key == card!.key);
    outgoing.add(FriendRequest(requestId, card, unixNow()));
    _changed();
    try {
      await Inbox.post(card, Notes.seal(me, card, 'request', {'C': me.friendCode, 'R': requestId, 'X': proof}));
    } catch (e) {
      return "Couldn't send the request: $e";
    }
    return proof != null ? 'Request sent. ${card.name} is added as soon as their Holm sees it' : 'Request sent to ${card.name}';
  }

  Future<void> answerRequest(FriendRequest request, bool accept) async {
    incoming.remove(request);
    if (accept) _addFriend(request.card);
    _changed();
    try {
      await Inbox.post(request.card, Notes.seal(me, request.card, 'response', {'R': request.requestId, 'A': accept}));
    } catch (e) {
      toast?.call("Couldn't answer: $e");
    }
  }

  Future<void> cancelRequest(FriendRequest request) async {
    outgoing.remove(request);
    _changed();
    try {
      await Inbox.post(request.card, Notes.seal(me, request.card, 'cancel-request', {'C': me.friendCode, 'R': request.requestId}));
    } catch (_) {}
  }

  Future<void> removeFriend(Friend f) async {
    friends.removeWhere((x) => x.key == f.key);
    chats.remove(f.key);
    unread.remove(f.key);
    _changed();
    try {
      await Inbox.post(f, Notes.seal(me, f, 'removed', {}));
    } catch (_) {}
  }

  void _addFriend(Friend card) {
    final existing = friendByKey(card.key);
    if (existing != null) friends.remove(existing);
    friends.add(card);
  }

  /// Splits a long text into messages of at most [maxChatBytes] UTF-8 bytes.
  static List<String> splitMessage(String text) {
    final parts = <String>[];
    var rest = text.trim();
    while (rest.isNotEmpty) {
      if (utf8.encode(rest).length <= maxChatBytes) {
        parts.add(rest);
        break;
      }
      var bytes = 0, cut = 0, lastSpace = -1;
      for (final rune in rest.runes) {
        final size = utf8.encode(String.fromCharCode(rune)).length;
        if (bytes + size > maxChatBytes) break;
        bytes += size;
        cut += rune > 0xffff ? 2 : 1;
        if (rune == 0x20 || rune == 0x0a) lastSpace = cut;
      }
      if (lastSpace > cut ~/ 2) cut = lastSpace;
      parts.add(rest.substring(0, cut).trim());
      rest = rest.substring(cut).trim();
    }
    return parts.where((p) => p.isNotEmpty).toList();
  }

  Future<void> sendChat(Friend f, String text) async {
    for (final part in splitMessage(text)) {
      final message = ChatMessage(id: newGuidN(), mine: true, text: part, sent: unixNow(), state: ChatState.sending);
      _addMessage(f.key, message, unreadToo: false);
      await _deliver(f, message);
    }
  }

  Future<void> retry(Friend f, ChatMessage m) async {
    m.state = ChatState.sending;
    _changed();
    await _deliver(f, m);
  }

  Future<void> _deliver(Friend f, ChatMessage m) async {
    try {
      await Inbox.post(f, Notes.seal(me, f, 'chat', {'M': Notes.chatText(m.text), 'I': m.id}));
      m.state = ChatState.sent;
    } catch (_) {
      m.state = ChatState.failed;
    }
    _changed();
  }

  Future<void> react(Friend f, ChatMessage m, String? emoji) async {
    m.myReaction = m.myReaction == emoji ? null : emoji;
    _changed();
    try {
      await Inbox.post(f, Notes.seal(me, f, 'react', {'I': m.id, 'M': m.myReaction == null ? null : Notes.chatText(m.myReaction!)}));
    } catch (_) {}
  }

  Future<void> wave(Friend f) async {
    try {
      await Inbox.post(f, Notes.seal(me, f, 'wave', {}), keep: false);
      toast?.call('You waved at ${f.name}');
    } catch (e) {
      toast?.call("Couldn't wave: $e");
    }
  }

  /// "Is your Holm running?" At most once a minute per friend.
  void askIfHere(Friend f) {
    final last = _pinged[f.key];
    if (last != null && DateTime.now().difference(last) < const Duration(minutes: 1)) return;
    _pinged[f.key] = DateTime.now();
    Inbox.post(f, Notes.seal(me, f, 'ping', {}), keep: false).catchError((_) {});
  }

  void _addMessage(String key, ChatMessage message, {required bool unreadToo}) {
    final list = chats.putIfAbsent(key, () => []);
    if (list.any((m) => m.id == message.id)) return;
    list.add(message);
    list.sort((a, b) => a.sent.compareTo(b.sent));
    if (list.length > keepMessages) list.removeRange(0, list.length - keepMessages);
    if (unreadToo) {
      unread[key] = (unread[key] ?? 0) + 1;
      if (!foreground) NotchLink.send({'type': 'holm.chat', 'name': friendByKey(key)?.name ?? 'Holm', 'text': message.text});
    }
    _changed();
  }

  // ───────────── our inbox ─────────────

  void startInbox() {
    if (_following) return;
    _following = true;
    _connect(const Duration(seconds: 2));
    _poll = Timer.periodic(const Duration(seconds: 60), (_) => _pollLegacy());
    _pollLegacy();
  }

  void stopInbox() {
    _following = false;
    _reconnect?.cancel();
    _poll?.cancel();
    _ws?.close();
    _ws = null;
  }

  Future<void> _connect(Duration backoff) async {
    if (!_following) return;
    try {
      final ws = await Inbox.follow(me.topic, _since);
      _ws = ws;
      inboxError = null;
      notifyListeners();
      for (final f in friends) {
        askIfHere(f);
      }
      announceDevice(force: true);
      await for (final line in ws) {
        if (line is String) _handleLine(line);
      }
      backoff = const Duration(seconds: 2);
    } catch (e) {
      inboxError = 'Chat is offline';
      notifyListeners();
    }
    _ws = null;
    if (!_following) return;
    _reconnect = Timer(backoff, () => _connect(Duration(seconds: (backoff.inSeconds * 2).clamp(2, 120))));
  }

  /// Older Holms (and a relay that is down) write to ntfy.sh, so that inbox is looked at too.
  Future<void> _pollLegacy() async {
    try {
      final events = await Inbox.poll(legacyServer, me.topic, '${_since ?? (unixNow() - 12 * 3600)}');
      for (final e in events) {
        _handleEvent(e);
      }
    } catch (_) {}
  }

  void _handleLine(String line) {
    final e = InboxEvent.tryParse(line);
    if (e != null) _handleEvent(e);
  }

  int _serverNow = 0;

  void _handleEvent(InboxEvent e) {
    if (e.time > _serverNow) _serverNow = e.time;
    if (!e.isMessage || e.id == null) return;
    if (_seen.contains(e.id)) return;
    _seen.add(e.id!);
    if (_seen.length > 200) _seen.removeRange(0, _seen.length - 200);
    if (e.time > (_since ?? 0)) _since = e.time;
    final text = e.text;
    if (text == null || !text.startsWith(Notes.prefix)) return;
    final opened = Notes.open(me, text);
    if (opened == null) return;
    final (note, sender) = opened;
    final age = (_serverNow - e.time).clamp(0, 1 << 31);
    _handleNote(note, sender, age);
    _changed();
  }

  void _handleNote(Note note, String sender, int age) {
    if (sender == me.publicKey) {
      _handleOwn(note, age); // sealed by our own identity: another device of the account
      return;
    }
    final friend = friendByKey(sender);
    void sawOnline() {
      if (friend != null && age < 180) lastSeen[friend.key] = DateTime.now();
    }

    switch (note.kind) {
      case 'chat' when friend != null:
        final text = note.text;
        final id = note.messageId;
        if (text == null || id == null) return;
        sawOnline();
        _addMessage(friend.key,
            ChatMessage(id: id, mine: false, text: text, sent: note.sent, state: ChatState.received),
            unreadToo: true);
      case 'react' when friend != null:
        final list = chats[friend.key] ?? [];
        for (final m in list) {
          if (m.id == note.messageId) m.theirReaction = note.text;
        }
      case 'request':
        final card = parseFriendCode(note.code);
        final id = note.requestId;
        if (card == null || card.key != sender || id == null) return;
        if (friend != null) {
          // Already friends (a one-sided friendship after a reinstall): say yes again.
          Inbox.post(card, Notes.seal(me, card, 'response', {'R': id, 'A': true})).catchError((_) {});
        } else if (!incoming.any((r) => r.requestId == id)) {
          incoming.add(FriendRequest(id, card, unixNow() - age));
          toast?.call('${card.name} would like to be friends');
        }
      case 'response':
        final match = outgoing.where((r) => r.requestId == note.requestId && r.card.key == sender).toList();
        if (match.isEmpty) return;
        outgoing.remove(match.first);
        if (note.accepted == true) {
          _addFriend(match.first.card);
          lastSeen[sender] = DateTime.now();
          toast?.call('${match.first.card.name} accepted your friend request');
        } else {
          toast?.call('${match.first.card.name} declined your friend request');
        }
      case 'cancel-request':
        incoming.removeWhere((r) => r.requestId == note.requestId && r.card.key == sender);
      case 'added':
        // Holm before friend requests: they added us. Treat it as a request to answer.
        final card = parseFriendCode(note.code);
        if (card != null && card.key == sender && friend == null) {
          incoming.add(FriendRequest(newGuidN(), card, unixNow() - age));
        }
      case 'removed' when friend != null:
        friends.remove(friend);
      case 'ping' when friend != null && age < 180:
        sawOnline();
        Inbox.post(friend, Notes.seal(me, friend, 'here', {}), keep: false).catchError((_) {});
      case 'waiting' when friend != null:
        sawOnline();
        Inbox.post(friend, Notes.seal(me, friend, 'here', {}), keep: false).catchError((_) {});
      case 'here' when friend != null:
        sawOnline();
      case 'wave' when friend != null && age < 600:
        sawOnline();
        toast?.call('${friend.name} waved 👋');
      case 'files' when friend != null && age < 300:
        toast?.call('${friend.name} wants to send files. On the same Wi-Fi, use Devices instead');
      default:
        break; // a kind this app doesn't handle, or from a stranger
    }
  }

  @override
  void dispose() {
    stopInbox();
    nearby.stop();
    super.dispose();
  }
}
