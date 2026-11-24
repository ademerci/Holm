import 'package:flutter/widgets.dart';

/// Holm's designs (Holm.Core/Design.cs) for the open notch: its background, text and accent. Closed, the notch stays
/// black whatever the design, like the camera it continues.
class NotchDesign {
  final String id, name;
  final int _panel, _ink, _accent;
  final int? _card, _dim;
  // ignore: prefer_initializing_formals
  const NotchDesign(this.id, this.name, {required int panel, required int ink, required int accent, int? card, int? dim})
      // ignore: prefer_initializing_formals
      : _panel = panel,
        // ignore: prefer_initializing_formals
        _ink = ink,
        // ignore: prefer_initializing_formals
        _accent = accent,
        // ignore: prefer_initializing_formals
        _card = card,
        // ignore: prefer_initializing_formals
        _dim = dim;

  Color get panel => Color(_panel);
  Color get ink => Color(_ink);
  Color get accent => Color(_accent);

  /// As in Design.cs: what a design leaves out comes from its text colour.
  Color get card => _card != null ? Color(_card) : ink.withAlpha(0x14);
  Color get dim => _dim != null ? Color(_dim) : ink.withAlpha(0x99);
  Color get faint => ink.withAlpha(0x66);

  /// The background, solid: the notch never lets the screen show through.
  Color get background => Color.alphaBlend(panel, const Color(0xFF000000));

  /// Text on the accent colour: white, or near black where white wouldn't read (Designs.OnColor).
  Color get onAccent => accent.computeLuminance() > 0.45 ? const Color(0xFF0B0B0C) : const Color(0xFFFFFFFF);

  static NotchDesign? byId(String? id) => id == null ? null : all.where((d) => d.id == id).firstOrNull;

  /// The 24 built-in designs, in the desktop's order.
  static const all = [
    NotchDesign('classic', 'Classic', panel: 0xFA1E1E20, ink: 0xFFFFFFFF, accent: 0xFF0A84FF),
    NotchDesign('instrument', 'Instrument', panel: 0xFF0B0B0C, card: 0xFF141416, ink: 0xFFF2F2F2, dim: 0xFF8A8C91, accent: 0xFFF2F2F2),
    NotchDesign('frosted', 'Frosted Glass', panel: 0xB8262A34, card: 0x1FFFFFFF, ink: 0xFFFFFFFF, accent: 0xFF7FB2FF),
    NotchDesign('neon', 'Neon', panel: 0xFF07040D, ink: 0xFFE9E4FF, dim: 0xFFA99BD6, accent: 0xFF22F0FF),
    NotchDesign('paper', 'Paper', panel: 0xFFFBF8F2, card: 0xFFFFFFFF, ink: 0xFF2B2724, dim: 0xFF7A6F63, accent: 0xFFC2410C),
    NotchDesign('terminal', 'Terminal', panel: 0xFF020402, ink: 0xFF39FF7A, dim: 0xFF1FAE4F, accent: 0xFF39FF7A),
    NotchDesign('brutalist', 'Brutalist', panel: 0xFFFFFFFF, card: 0xFFFFFFFF, ink: 0xFF000000, dim: 0xFF333333, accent: 0xFFFF4D00),
    NotchDesign('aurora', 'Aurora', panel: 0xFF0B0C16, card: 0x0DFFFFFF, ink: 0xFFFFFFFF, accent: 0xFF03E1FF),
    NotchDesign('prism', 'Prism Glass', panel: 0xCDE8ECF1, card: 0x40FFFFFF, ink: 0xFF121A24, dim: 0xFF273344, accent: 0xFF005CAA),
    NotchDesign('material', 'Material', panel: 0xFFF7F2FA, card: 0xFFECE6F0, ink: 0xFF1D192B, dim: 0xFF49454F, accent: 0xFF6750A4),
    NotchDesign('mica', 'Mica', panel: 0xD8202834, card: 0x0DFFFFFF, ink: 0xFFFFFFFF, accent: 0xFF60CDFF),
    NotchDesign('dotmatrix', 'Dot Matrix', panel: 0xFF000000, ink: 0xFFFFFFFF, dim: 0xFF8A8A8A, accent: 0xFFD71921),
    NotchDesign('hardware', 'Hardware', panel: 0xFFE4E4DF, ink: 0xFF1A1A1A, dim: 0xFF5A5A55, accent: 0xFFFF5A00),
    NotchDesign('aero', 'Frutiger Aero', panel: 0xFFDFF6FF, card: 0xFFF4FCFF, ink: 0xFF0A2B43, dim: 0xFF214A63, accent: 0xFF075C9D),
    NotchDesign('retro98', 'Retro 98', panel: 0xFFC0C0C0, card: 0xFFC0C0C0, ink: 0xFF000000, dim: 0xFF404040, accent: 0xFF000080),
    NotchDesign('pocket', 'Pocket', panel: 0xFF9BBC0F, ink: 0xFF0F380F, dim: 0xFF306230, accent: 0xFF0F380F),
    NotchDesign('album', 'Album', panel: 0xFF181818, card: 0xFF242424, ink: 0xFFFFFFFF, dim: 0xFFB3B3B3, accent: 0xFF1ED760),
    NotchDesign('blueprint', 'Blueprint', panel: 0xFF163557, card: 0xFF1C4063, ink: 0xFFE6F5FC, dim: 0xFFB2CFDF, accent: 0xFFA8EDFF),
    NotchDesign('matcha', 'Matcha', panel: 0xFFE6EDDD, card: 0xFFF3F5EA, ink: 0xFF203C30, dim: 0xFF405B4C, accent: 0xFF35634A),
    NotchDesign('kiln', 'Kiln', panel: 0xFF432B27, card: 0xFF53352E, ink: 0xFFFFEFDF, dim: 0xFFE5C6B4, accent: 0xFFFFBA86),
    NotchDesign('nocturne', 'Nocturne', panel: 0xFF2C2438, card: 0xFF393044, ink: 0xFFF0E6F9, dim: 0xFFCCBFD6, accent: 0xFFD6B2EE),
    NotchDesign('signalbox', 'Signal Box', panel: 0xFFFFE6A2, card: 0xFFFFF0C8, ink: 0xFF241F19, dim: 0xFF554731, accent: 0xFF9F2B1E),
    NotchDesign('margin', 'Margin Notes', panel: 0xFFF7F3EA, card: 0xFFFCFAF4, ink: 0xFF302923, dim: 0xFF5F5146, accent: 0xFFA24429),
    NotchDesign('commanddeck', 'Command Deck', panel: 0xFF191D24, card: 0xFF20262F, ink: 0xFFE8EEF5, dim: 0xFFB6C1CF, accent: 0xFF8CC8FF),
  ];
}

/// The colours the open notch's content draws with: the design's, or Holm's white on black.
class NotchPalette extends InheritedWidget {
  final Color ink, dim, faint, accent, onAccent, card;
  const NotchPalette({
    super.key,
    required this.ink,
    required this.dim,
    required this.faint,
    required this.accent,
    required this.onAccent,
    required this.card,
    required super.child,
  });

  factory NotchPalette.forDesign(NotchDesign? d, {required Widget child}) => d == null
      ? NotchPalette(
          ink: const Color(0xFFFFFFFF),
          dim: const Color(0x99FFFFFF),
          faint: const Color(0x66FFFFFF),
          accent: const Color(0xFF0A84FF),
          onAccent: const Color(0xFFFFFFFF),
          card: const Color(0x1FFFFFFF),
          child: child,
        )
      : NotchPalette(ink: d.ink, dim: d.dim, faint: d.faint, accent: d.accent, onAccent: d.onAccent, card: d.card, child: child);

  static const _plain = NotchPalette(
    ink: Color(0xFFFFFFFF),
    dim: Color(0x99FFFFFF),
    faint: Color(0x66FFFFFF),
    accent: Color(0xFF0A84FF),
    onAccent: Color(0xFFFFFFFF),
    card: Color(0x1FFFFFFF),
    child: SizedBox.shrink(),
  );

  static NotchPalette of(BuildContext context) => context.dependOnInheritedWidgetOfExactType<NotchPalette>() ?? _plain;

  @override
  bool updateShouldNotify(NotchPalette old) =>
      ink != old.ink || dim != old.dim || accent != old.accent || card != old.card || faint != old.faint;
}
