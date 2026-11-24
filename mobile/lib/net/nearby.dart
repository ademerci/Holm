// Holm.Core/Sharing/NearbyService.cs in Dart: finds Holm apps (desktop and mobile) on the same network with a small
// UDP broadcast on port 47811, and accepts their encrypted transfers over TCP.

import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'crypto.dart';
import 'secure_channel.dart';

class NearbyPeer {
  final String id;
  final String name;
  final InternetAddress address;
  final int port;
  final DateTime lastSeen;
  const NearbyPeer(this.id, this.name, this.address, this.port, this.lastSeen);
}

class NearbyService {
  static const discoveryPort = 47811;
  static const _app = 'WinNotch'; // the desktop app's former name, still what every Holm announces
  static const _lifetime = Duration(minutes: 3);

  final String id = newGuid();
  String name;
  final Map<String, NearbyPeer> _peers = {};
  final _changes = StreamController<void>.broadcast();
  final Set<String> _manual = {}; // addresses added by hand, for networks that drop broadcasts

  RawDatagramSocket? _udp;
  ServerSocket? _tcp;
  Timer? _announce;
  bool _busy = false;

  /// Handles an incoming connection. Only one at a time; others are turned away meanwhile.
  Future<void> Function(SecureChannel channel)? incoming;

  NearbyService(this.name);

  Stream<void> get changes => _changes.stream;
  bool get isRunning => _udp != null;
  int get tcpPort => _tcp?.port ?? 0;

  List<NearbyPeer> get peers {
    final now = DateTime.now();
    final list = _peers.values.where((p) => now.difference(p.lastSeen) < _lifetime).toList()
      ..sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
    return list;
  }

  Future<void> start() async {
    if (_udp != null) return;
    _tcp = await ServerSocket.bind(InternetAddress.anyIPv4, 0);
    _tcp!.listen(_accept, onError: (_) {});
    _udp = await RawDatagramSocket.bind(InternetAddress.anyIPv4, discoveryPort, reuseAddress: true);
    _udp!.broadcastEnabled = true;
    _udp!.listen(_onUdp, onError: (_) {});
    await _broadcast('query');
    _announce = Timer.periodic(const Duration(seconds: 60), (_) {
      _broadcast('hello');
      _prune();
    });
  }

  Future<void> stop() async {
    if (_udp == null) return;
    try {
      await _broadcast('bye');
    } catch (_) {}
    _announce?.cancel();
    _udp?.close();
    _udp = null;
    await _tcp?.close();
    _tcp = null;
    if (_peers.isNotEmpty) {
      _peers.clear();
      _changes.add(null);
    }
  }

  /// Asks everyone to answer now.
  Future<void> refresh() async {
    await _broadcast('query');
    await Future<void>.delayed(const Duration(milliseconds: 700));
    _prune();
  }

  /// Asks one address directly (a laptop on a network that drops broadcasts, or the emulator's host at 10.0.2.2).
  Future<void> addAddress(String host) async {
    final address = InternetAddress(host.trim());
    _manual.add(address.address);
    await _send('query', [address]);
  }

  void _prune() {
    final now = DateTime.now();
    final before = _peers.length;
    _peers.removeWhere((_, p) => now.difference(p.lastSeen) >= _lifetime);
    if (_peers.length != before) _changes.add(null);
  }

  List<int> _packet(String type) =>
      utf8.encode(jsonEncode({'App': _app, 'V': 1, 'T': type, 'Id': id, 'Name': name, 'Port': tcpPort}));

  Future<void> _broadcast(String type) async {
    final targets = <String>{'255.255.255.255'};
    try {
      for (final nic in await NetworkInterface.list(type: InternetAddressType.IPv4)) {
        for (final a in nic.addresses) {
          if (a.isLoopback || a.isLinkLocal) continue;
          final parts = a.address.split('.');
          // Dart doesn't give the netmask; home and office Wi-Fi is nearly always a /24.
          targets.add('${parts[0]}.${parts[1]}.${parts[2]}.255');
        }
      }
    } catch (_) {}
    targets.addAll(_manual);
    await _send(type, targets.map(InternetAddress.new));
  }

  Future<void> _send(String type, Iterable<InternetAddress> targets) async {
    final udp = _udp;
    if (udp == null) return;
    final bytes = _packet(type);
    for (final t in targets) {
      try {
        udp.send(bytes, t, discoveryPort);
      } catch (_) {} // an interface that went away
    }
  }

  void _onUdp(RawSocketEvent event) {
    if (event != RawSocketEvent.read) return;
    final udp = _udp;
    final datagram = udp?.receive();
    if (udp == null || datagram == null || datagram.data.length > 2048) return;
    Map<String, dynamic> packet;
    try {
      packet = jsonDecode(utf8.decode(datagram.data)) as Map<String, dynamic>;
    } catch (_) {
      return;
    }
    if (packet['App'] != _app || packet['V'] != 1 || packet['Id'] is! String) return;
    final peerId = (packet['Id'] as String).toLowerCase();
    if (peerId == id) return;
    if (packet['T'] == 'bye') {
      if (_peers.remove(peerId) != null) _changes.add(null);
      return;
    }
    final port = packet['Port'];
    if (port is! int || port <= 0 || port > 65535) return;
    var peerName = ((packet['Name'] as String?) ?? '')
        .runes
        .where((r) => r >= 0x20)
        .take(40)
        .map(String.fromCharCode)
        .join()
        .trim();
    if (peerName.isEmpty) peerName = 'Holm';
    final isNew = !_peers.containsKey(peerId);
    _peers[peerId] = NearbyPeer(peerId, peerName, datagram.address, port, DateTime.now());
    if (isNew) _changes.add(null);
    if (packet['T'] == 'query') {
      try {
        udp.send(_packet('hello'), datagram.address, datagram.port);
      } catch (_) {}
    }
  }

  Future<void> _accept(Socket socket) async {
    if (_busy) {
      socket.destroy(); // already busy with someone
      return;
    }
    _busy = true;
    SecureChannel? channel;
    try {
      socket.setOption(SocketOption.tcpNoDelay, true);
      channel = await SecureChannel.open(socket, initiator: false).timeout(const Duration(seconds: 10));
      final handler = incoming;
      if (handler != null) await handler(channel);
    } catch (_) {
      // a failed handshake or a dropped connection
    } finally {
      await channel?.close();
      socket.destroy();
      _busy = false;
    }
  }

  static Future<SecureChannel> connect(NearbyPeer peer) => SecureChannel.connect(peer.address, peer.port);
}
