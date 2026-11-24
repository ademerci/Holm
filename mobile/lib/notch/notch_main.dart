import 'package:flutter/services.dart';
import 'package:material_ui/material_ui.dart';

import 'activities.dart';
import 'notch_view.dart';
import 'settings.dart';

/// The notch's own engine, drawn in the accessibility overlay (android/.../NotchService.kt).
Future<void> runNotch() async {
  WidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('holm/notch');
  final controller = NotchController();
  controller.action = (method, args) => channel.invokeMethod<Object?>(method, args).catchError((_) => null);
  channel.setMethodCallHandler((call) async {
    if (call.method != 'event') return;
    final e = Map<String, Object?>.from(call.arguments as Map);
    try {
      controller.handle(e);
    } catch (error, stack) {
      debugPrint('Notch: ${e['type']} failed: $error $stack');
    }
  });
  runApp(NotchApp(controller, onWindow: (w, h, touchable, rect) {
    channel.invokeMethod('window', {'w': w, 'h': h, 'touchable': touchable, 'rect': rect}).catchError((Object e) => debugPrint('Notch: window failed $e'));
  }));
  try {
    final init = await channel.invokeMethod<Map>('ready');
    controller.landscape = init?['landscape'] == true;
    controller.touchRegion = init?['touchRegion'] == true;
    controller.setSettings(NotchSettings.fromJsonString(init?['settings'] as String?));
  } catch (e) {
    debugPrint('Notch: ready failed $e');
  }
}

class NotchApp extends StatelessWidget {
  final NotchController controller;
  final void Function(double, double, bool, List<double>?)? onWindow;
  const NotchApp(this.controller, {super.key, this.onWindow});

  @override
  Widget build(BuildContext context) => MaterialApp(
        debugShowCheckedModeBanner: false,
        color: Colors.transparent,
        theme: ThemeData(brightness: Brightness.dark, canvasColor: Colors.transparent, scaffoldBackgroundColor: Colors.transparent),
        home: Material(type: MaterialType.transparency, child: NotchStage(controller, onWindow: onWindow)),
      );
}
