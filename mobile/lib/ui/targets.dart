import 'package:material_ui/material_ui.dart';

import '../app_state.dart';
import 'theme.dart';
import 'widgets.dart';

/// Asks where something goes: a device of the account (anywhere) or a Holm on this Wi-Fi. With only one, takes it.
Future<SendTarget?> pickTarget(BuildContext context, AppState state, {String title = 'Send to'}) async {
  var targets = state.targets;
  if (targets.isEmpty) {
    state.nearby.refresh();
    state.announceDevice(force: true);
    for (var i = 0; i < 10 && state.targets.isEmpty; i++) {
      await Future<void>.delayed(const Duration(milliseconds: 250));
    }
    targets = state.targets;
  }
  if (!context.mounted) return null;
  if (targets.isEmpty) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(state.account == null
            ? 'No device found. Open Holm on your laptop on this Wi-Fi, or sign in to reach it anywhere'
            : 'No device found. Open Holm on your laptop')));
    return null;
  }
  if (targets.length == 1) return targets.first;
  return showModalBottomSheet<SendTarget>(
    context: context,
    builder: (context) => SafeArea(
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Text(title, style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w600)),
        ),
        for (final t in targets)
          ListTile(
            leading: Avatar(t.name, size: 36, icon: Icons.laptop_rounded),
            title: Text(t.name),
            subtitle: Text(t.peer?.address.address ?? 'Anywhere · Holm account', style: const TextStyle(color: HolmColors.dim)),
            onTap: () => Navigator.pop(context, t),
          ),
        const SizedBox(height: 12),
      ]),
    ),
  );
}
