import 'package:material_ui/material_ui.dart';

/// Holm's Classic design (Holm.Core/Design.cs, Holm.App/HubTheme.cs): dark panels, white ink at set alphas,
/// the blue accent, and the blue-to-violet gradient of a sent chat message.
class HolmColors {
  static const ink = Color(0xFFFFFFFF);
  static const panel = Color(0xFF1E1E20);
  static const sidebar = Color(0xFF171719);
  static const background = Color(0xFF111113);
  static const card = Color(0x14FFFFFF);
  static const cardStrong = Color(0x1FFFFFFF);
  static const selected = Color(0x26FFFFFF);
  static const line = Color(0x14FFFFFF);
  static const dim = Color(0x99FFFFFF);
  static const faint = Color(0x66FFFFFF);
  static const accent = Color(0xFF0A84FF);
  static const green = Color(0xFF30D158);
  static const red = Color(0xFFFF453A);
  static const gold = Color(0xFFFFC43D);
  static const raised = Color(0xFF2E2E30);

  static const sentGradient = LinearGradient(colors: [Color(0xFF2E7BFF), Color(0xFF7B4DFF)]);

  /// The cover wash of the open notch in the screenshots: warm red into violet with an amber glow.
  static const wash = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF8E2846), Color(0xFF6E1F52), Color(0xFF451457)],
  );
}

ThemeData holmTheme() {
  final base = ThemeData(
    brightness: Brightness.dark,
    useMaterial3: true,
    colorScheme: const ColorScheme.dark(
      primary: HolmColors.accent,
      secondary: HolmColors.accent,
      surface: HolmColors.panel,
      error: HolmColors.red,
      onPrimary: Colors.white,
      onSurface: HolmColors.ink,
    ),
    scaffoldBackgroundColor: HolmColors.background,
    splashFactory: InkSparkle.splashFactory,
  );
  return base.copyWith(
    textTheme: base.textTheme.apply(bodyColor: HolmColors.ink, displayColor: HolmColors.ink),
    appBarTheme: const AppBarTheme(
      backgroundColor: HolmColors.background,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      titleTextStyle: TextStyle(fontSize: 20, fontWeight: FontWeight.w600, color: HolmColors.ink),
    ),
    dialogTheme: const DialogThemeData(
      backgroundColor: HolmColors.panel,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(22))),
    ),
    bottomSheetTheme: const BottomSheetThemeData(
      backgroundColor: HolmColors.panel,
      surfaceTintColor: Colors.transparent,
      showDragHandle: true,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(24))),
    ),
    snackBarTheme: const SnackBarThemeData(
      backgroundColor: HolmColors.raised,
      contentTextStyle: TextStyle(color: HolmColors.ink),
      behavior: SnackBarBehavior.floating,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(14))),
    ),
    inputDecorationTheme: const InputDecorationTheme(
      filled: true,
      fillColor: HolmColors.card,
      hintStyle: TextStyle(color: HolmColors.faint),
      border: OutlineInputBorder(borderRadius: BorderRadius.all(Radius.circular(14)), borderSide: BorderSide.none),
      contentPadding: EdgeInsets.symmetric(horizontal: 16, vertical: 14),
    ),
    switchTheme: SwitchThemeData(
      trackColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? HolmColors.green : HolmColors.cardStrong),
      thumbColor: const WidgetStatePropertyAll(Colors.white),
      trackOutlineColor: const WidgetStatePropertyAll(Colors.transparent),
    ),
    dividerTheme: const DividerThemeData(color: HolmColors.line, thickness: 1, space: 1),
  );
}
