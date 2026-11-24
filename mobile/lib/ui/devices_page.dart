import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:material_ui/material_ui.dart';
import 'package:flutter/services.dart';
import 'package:open_filex/open_filex.dart';
import 'package:share_plus/share_plus.dart';

import '../app_state.dart';
import 'account_page.dart';
import 'theme.dart';
import 'widgets.dart';

/// Where things can go: the devices of your Holm account (anywhere) and Holm apps on the same Wi-Fi, and what arrived.
class DevicesPage extends StatefulWidget {
  final AppState state;
  const DevicesPage(this.state, {super.key});

  @override
  State<DevicesPage> createState() => _DevicesPageState();
}

class _DevicesPageState extends State<DevicesPage> {
  String? _address;
  bool _refreshing = false;

  AppState get s => widget.state;

  @override
  void initState() {
    super.initState();
    _findAddress();
  }

  Future<void> _findAddress() async {
    try {
      for (final nic in await NetworkInterface.list(type: InternetAddressType.IPv4)) {
        for (final a in nic.addresses) {
          if (!a.isLoopback && !a.isLinkLocal) {
            if (mounted) setState(() => _address = a.address);
            return;
          }
        }
      }
    } catch (_) {}
  }

  Future<void> _refresh() async {
    setState(() => _refreshing = true);
    s.announceDevice(force: true);
    await s.nearby.refresh();
    await _findAddress();
    if (mounted) setState(() => _refreshing = false);
  }

  Future<void> _sendFiles(SendTarget peer) async {
    final picked = await FilePicker.pickFiles();
    final paths = picked.map((f) => f.path).whereType<String>().toList();
    if (paths.isEmpty) return;
    await s.sendFiles(peer, paths);
  }

  Future<void> _sendClipboard(SendTarget peer) async {
    final text = (await Clipboard.getData(Clipboard.kTextPlain))?.text;
    if (text == null || text.trim().isEmpty) {
      _snack('Your clipboard is empty');
      return;
    }
    await s.sendText(peer, text);
  }

  void _snack(String text) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));

  Future<void> _addAddress() async {
    final controller = TextEditingController();
    final host = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Find a device by address'),
        content: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('For a network that hides devices from each other: type the IP address of the laptop running Holm.',
              style: TextStyle(color: HolmColors.dim)),
          const SizedBox(height: 14),
          TextField(
            controller: controller,
            autofocus: true,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(hintText: '192.168.1.23'),
          ),
        ]),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, controller.text), child: const Text('Find')),
        ],
      ),
    );
    if (host == null || host.trim().isEmpty) return;
    try {
      await s.addAddress(host);
      await Future<void>.delayed(const Duration(milliseconds: 800));
      if (mounted) setState(() {});
    } catch (_) {
      _snack("That isn't an IP address");
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: s,
        builder: (context, _) {
          final peers = s.nearby.peers;
          final nearbyNames = {for (final p in peers) p.name};
          final devices = s.ownDevices.where((d) => !nearbyNames.contains(d.name)).toList();
          Widget card(SendTarget t) => _PeerCard(
                target: t,
                syncing: s.clipboardSync && s.syncTarget == t.name,
                onFiles: () => _sendFiles(t),
                onClipboard: () => _sendClipboard(t),
              );
          return RefreshIndicator(
            onRefresh: _refresh,
            child: ListView(
              padding: const EdgeInsets.only(bottom: 24),
              children: [
                PageHeader('Devices', 'Beam files and text to your laptop, at home or anywhere',
                    trailing: RoundIconButton(Icons.refresh_rounded, tooltip: 'Look again', onPressed: _refreshing ? null : _refresh)),
                _IdentityCard(name: s.deviceName, address: _address, running: s.nearby.isRunning),
                if (s.transfer != null) _TransferBanner(s.transfer!, onClose: s.dismissTransfer),
                SectionLabel('Your devices',
                    trailing: s.account == null
                        ? null
                        : TextButton(onPressed: () => showAccount(context, s), child: const Text('Account'))),
                if (s.account == null)
                  _SignInCard(onSignIn: () => showAccount(context, s))
                else if (devices.isEmpty)
                  EmptyState(Icons.devices_rounded, 'No device of yours is online',
                      s.accountError ?? 'Open Holm on a PC signed in to ${s.account!.email}. It shows up here, on any network.')
                else
                  for (final d in devices) card(SendTarget.device(d)),
                SectionLabel('On this Wi-Fi', trailing: TextButton.icon(
                  onPressed: _addAddress,
                  icon: const Icon(Icons.add_rounded, size: 18),
                  label: const Text('By address'),
                )),
                if (peers.isEmpty)
                  const EmptyState(Icons.wifi_find_rounded, 'Looking for Holm…',
                      'Open Holm on your laptop (with Beam on) or this app on another phone, on the same Wi-Fi.'),
                for (final peer in peers) card(SendTarget.nearby(peer)),
                if (s.received.isNotEmpty) ...[
                  const SectionLabel('Received'),
                  for (final file in s.received.take(30)) _ReceivedTile(file: file, state: s),
                ],
              ],
            ),
          );
        },
      );
}

class _IdentityCard extends StatelessWidget {
  final String name;
  final String? address;
  final bool running;
  const _IdentityCard({required this.name, required this.address, required this.running});

  @override
  Widget build(BuildContext context) => Container(
        margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(gradient: HolmColors.wash, borderRadius: BorderRadius.circular(24)),
        child: Row(children: [
          Container(
            width: 52,
            height: 52,
            decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.14), borderRadius: BorderRadius.circular(16)),
            child: const Icon(Icons.smartphone_rounded, color: Colors.white, size: 28),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(name, style: const TextStyle(fontSize: 19, fontWeight: FontWeight.w600)),
              const SizedBox(height: 3),
              Text(
                running ? 'Visible to Holm${address != null ? ' · $address' : ''}' : 'Nearby sharing is off',
                style: TextStyle(color: Colors.white.withValues(alpha: 0.7)),
              ),
            ]),
          ),
          Container(
            width: 10,
            height: 10,
            decoration: BoxDecoration(shape: BoxShape.circle, color: running ? HolmColors.green : HolmColors.red),
          ),
        ]),
      );
}

class _TransferBanner extends StatelessWidget {
  final TransferStatus status;
  final VoidCallback onClose;
  const _TransferBanner(this.status, {required this.onClose});

  @override
  Widget build(BuildContext context) => HolmCard(
        color: HolmColors.cardStrong,
        margin: const EdgeInsets.fromLTRB(16, 10, 16, 0),
        child: Row(children: [
          Icon(
            status.failed ? Icons.error_outline_rounded : (status.done ? Icons.check_circle_rounded : Icons.send_rounded),
            color: status.failed ? HolmColors.red : (status.done ? HolmColors.green : HolmColors.accent),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(status.title, style: const TextStyle(fontWeight: FontWeight.w600)),
              const SizedBox(height: 2),
              Text(status.detail, style: const TextStyle(color: HolmColors.dim, fontSize: 13)),
              if (!status.done) ...[
                const SizedBox(height: 10),
                ClipRRect(
                  borderRadius: BorderRadius.circular(4),
                  child: LinearProgressIndicator(
                    value: status.progress,
                    minHeight: 5,
                    backgroundColor: HolmColors.card,
                    color: HolmColors.accent,
                  ),
                ),
              ],
            ]),
          ),
          if (status.done) RoundIconButton(Icons.close_rounded, size: 36, onPressed: onClose),
        ]),
      );
}

class _SignInCard extends StatelessWidget {
  final VoidCallback onSignIn;
  const _SignInCard({required this.onSignIn});

  @override
  Widget build(BuildContext context) => HolmCard(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 14),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Row(children: [
            Icon(Icons.public_rounded, color: HolmColors.accent),
            SizedBox(width: 12),
            Expanded(child: Text('Reach your laptop from anywhere', style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600))),
          ]),
          const SizedBox(height: 6),
          const Text('Sign in with your Holm account. Your PCs signed in to it show up here on any network, not only on this Wi-Fi.',
              style: TextStyle(color: HolmColors.dim, fontSize: 13.5, height: 1.35)),
          const SizedBox(height: 12),
          PillButton('Sign in', icon: Icons.person_outline_rounded, onPressed: onSignIn),
        ]),
      );
}

class _PeerCard extends StatelessWidget {
  final SendTarget target;
  final bool syncing;
  final VoidCallback onFiles;
  final VoidCallback onClipboard;
  const _PeerCard({required this.target, required this.syncing, required this.onFiles, required this.onClipboard});

  @override
  Widget build(BuildContext context) {
    final name = target.name;
    final phone = RegExp('phone|pixel|galaxy|android|iphone', caseSensitive: false).hasMatch(name);
    final where = target.peer?.address.address ?? 'Anywhere · Holm account';
    return HolmCard(
      padding: const EdgeInsets.fromLTRB(16, 14, 12, 14),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Avatar(name, size: 44, icon: phone ? Icons.smartphone_rounded : Icons.laptop_rounded, online: true),
          const SizedBox(width: 14),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(name, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
              const SizedBox(height: 2),
              Text(syncing ? 'Clipboard sync on · $where' : where,
                  style: const TextStyle(color: HolmColors.dim, fontSize: 13)),
            ]),
          ),
        ]),
        const SizedBox(height: 12),
        Row(children: [
          PillButton('Send files', icon: Icons.upload_file_rounded, onPressed: onFiles),
          const SizedBox(width: 8),
          PillButton('Send clipboard', icon: Icons.content_paste_go_rounded, onPressed: onClipboard, primary: false),
        ]),
      ]),
    );
  }
}

class _ReceivedTile extends StatelessWidget {
  final ReceivedFile file;
  final AppState state;
  const _ReceivedTile({required this.file, required this.state});

  @override
  Widget build(BuildContext context) {
    final f = File(file.path);
    final size = f.existsSync() ? fileSize(f.lengthSync()) : 'gone';
    return HolmCard(
      padding: const EdgeInsets.fromLTRB(16, 10, 6, 10),
      onTap: () => OpenFilex.open(file.path),
      child: Row(children: [
        const Icon(Icons.insert_drive_file_outlined, color: HolmColors.dim),
        const SizedBox(width: 12),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(file.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontWeight: FontWeight.w500)),
            Text('$size · from ${file.from} · ${timeAgo(file.time)}', style: const TextStyle(color: HolmColors.dim, fontSize: 12.5)),
          ]),
        ),
        RoundIconButton(Icons.ios_share_rounded, size: 38, tooltip: 'Share',
            onPressed: () => SharePlus.instance.share(ShareParams(files: [XFile(file.path)]))),
        RoundIconButton(Icons.close_rounded, size: 38, tooltip: 'Delete', onPressed: () => state.deleteReceived(file)),
      ]),
    );
  }
}
