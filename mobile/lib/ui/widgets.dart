import 'package:material_ui/material_ui.dart';

import 'theme.dart';

/// The big title and grey line under it that every Holm hub page starts with.
class PageHeader extends StatelessWidget {
  final String title;
  final String subtitle;
  final Widget? trailing;
  const PageHeader(this.title, this.subtitle, {super.key, this.trailing});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(20, 12, 12, 16),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(title, style: const TextStyle(fontSize: 32, fontWeight: FontWeight.w600, height: 1.1)),
                const SizedBox(height: 6),
                Text(subtitle, style: const TextStyle(fontSize: 15, color: HolmColors.dim)),
              ]),
            ),
            ?trailing,
          ],
        ),
      );
}

/// A small upper-case label above a group, as "FRIENDS" and "SETTINGS" in the hub's sidebar.
class SectionLabel extends StatelessWidget {
  final String text;
  final Widget? trailing;
  const SectionLabel(this.text, {super.key, this.trailing});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(20, 18, 12, 8),
        child: Row(children: [
          Expanded(
            child: Text(text.toUpperCase(),
                style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: HolmColors.dim, letterSpacing: 0.4)),
          ),
          ?trailing,
        ]),
      );
}

/// A translucent rounded card.
class HolmCard extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final EdgeInsetsGeometry margin;
  final VoidCallback? onTap;
  final VoidCallback? onLongPress;
  final Color color;
  final double radius;
  const HolmCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.margin = const EdgeInsets.symmetric(horizontal: 16, vertical: 5),
    this.onTap,
    this.onLongPress,
    this.color = HolmColors.card,
    this.radius = 18,
  });

  @override
  Widget build(BuildContext context) => Padding(
        padding: margin,
        child: Material(
          color: color,
          borderRadius: BorderRadius.circular(radius),
          clipBehavior: Clip.antiAlias,
          child: InkWell(onTap: onTap, onLongPress: onLongPress, child: Padding(padding: padding, child: child)),
        ),
      );
}

/// A round face: initials on a gradient chosen by the name, with an optional green "online" dot.
class Avatar extends StatelessWidget {
  final String name;
  final double size;
  final bool online;
  final IconData? icon;
  const Avatar(this.name, {super.key, this.size = 48, this.online = false, this.icon});

  static const _gradients = [
    [Color(0xFFFF9F45), Color(0xFFFF5E7E)],
    [Color(0xFFC86DD7), Color(0xFFF06292)],
    [Color(0xFF5E5CE6), Color(0xFF7B4DFF)],
    [Color(0xFFFF9F0A), Color(0xFFFFB340)],
    [Color(0xFF30B0C7), Color(0xFF34C759)],
    [Color(0xFF0A84FF), Color(0xFF64D2FF)],
  ];

  @override
  Widget build(BuildContext context) {
    final hash = name.codeUnits.fold<int>(7, (h, c) => (h * 31 + c) & 0x7fffffff);
    final colors = _gradients[hash % _gradients.length];
    final letters = name.trim().split(RegExp(r'\s+')).where((w) => w.isNotEmpty).take(2).map((w) => w.characters.first.toUpperCase()).join();
    return SizedBox(
      width: size,
      height: size,
      child: Stack(clipBehavior: Clip.none, children: [
        Container(
          width: size,
          height: size,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            gradient: LinearGradient(begin: Alignment.topLeft, end: Alignment.bottomRight, colors: colors),
          ),
          alignment: Alignment.center,
          child: icon != null
              ? Icon(icon, color: Colors.white, size: size * 0.48)
              : Text(letters.isEmpty ? '?' : letters,
                  style: TextStyle(fontSize: size * 0.38, fontWeight: FontWeight.w600, color: Colors.white)),
        ),
        if (online)
          Positioned(
            right: -1,
            bottom: -1,
            child: Container(
              width: size * 0.3,
              height: size * 0.3,
              decoration: BoxDecoration(
                color: HolmColors.green,
                shape: BoxShape.circle,
                border: Border.all(color: HolmColors.background, width: 2.5),
              ),
            ),
          ),
      ]),
    );
  }
}

/// The round tool buttons of the open notch: a soft circle behind the selected one.
class RoundIconButton extends StatelessWidget {
  final IconData icon;
  final VoidCallback? onPressed;
  final String? tooltip;
  final bool filled;
  final Color? color;
  final double size;
  const RoundIconButton(this.icon, {super.key, this.onPressed, this.tooltip, this.filled = false, this.color, this.size = 44});

  @override
  Widget build(BuildContext context) => Tooltip(
        message: tooltip ?? '',
        child: Material(
          color: filled ? HolmColors.cardStrong : Colors.transparent,
          shape: const CircleBorder(),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            onTap: onPressed,
            child: SizedBox(
              width: size,
              height: size,
              child: Icon(icon, size: size * 0.5, color: onPressed == null ? HolmColors.faint : (color ?? HolmColors.dim)),
            ),
          ),
        ),
      );
}

/// Holm's primary action: a blue pill; secondary actions are the grey card colour.
class PillButton extends StatelessWidget {
  final String label;
  final IconData? icon;
  final VoidCallback? onPressed;
  final bool primary;
  const PillButton(this.label, {super.key, this.icon, this.onPressed, this.primary = true});

  /// Its colour, fainter without an action (keeping the colour's own transparency: the secondary one is see-through).
  Color get _fill {
    final c = primary ? HolmColors.accent : HolmColors.cardStrong;
    return onPressed == null ? c.withValues(alpha: c.a * 0.4) : c;
  }

  @override
  Widget build(BuildContext context) => Material(
        // Without an action (busy, or not possible yet) it fades back.
        color: _fill,
        borderRadius: BorderRadius.circular(22),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onPressed,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
            child: Row(mainAxisSize: MainAxisSize.min, children: [
              if (icon != null) ...[Icon(icon, size: 18, color: Colors.white), const SizedBox(width: 8)],
              Text(label, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14, color: Colors.white)),
            ]),
          ),
        ),
      );
}

class EmptyState extends StatelessWidget {
  final IconData icon;
  final String title;
  final String text;
  const EmptyState(this.icon, this.title, this.text, {super.key});

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 40),
        child: Column(children: [
          Icon(icon, size: 44, color: HolmColors.faint),
          const SizedBox(height: 14),
          Text(title, style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w600)),
          const SizedBox(height: 6),
          Text(text, textAlign: TextAlign.center, style: const TextStyle(color: HolmColors.dim, height: 1.35)),
        ]),
      );
}

String timeAgo(DateTime t) {
  final d = DateTime.now().difference(t);
  if (d.inSeconds < 60) return 'now';
  if (d.inMinutes < 60) return '${d.inMinutes} min';
  if (d.inHours < 24) return '${d.inHours} h';
  return '${d.inDays} d';
}

String clock(DateTime t) => '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';

String fileSize(int bytes) {
  if (bytes < 1024) return '$bytes B';
  if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
  if (bytes < 1024 * 1024 * 1024) return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
  return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
}
