import 'package:flutter/painting.dart';

/// Well-known apps get their colour in the notch (a ring around their icon, the accent of the open card). The icon itself
/// is the app's own, taken from the phone.
class Brand {
  final String name;
  final Color color;
  final Color? second; // a second colour for a gradient (Instagram)
  const Brand(this.name, this.color, [this.second]);

  static const _known = <String, Brand>{
    'com.whatsapp': Brand('WhatsApp', Color(0xFF25D366)),
    'com.whatsapp.w4b': Brand('WhatsApp Business', Color(0xFF25D366)),
    'org.telegram.messenger': Brand('Telegram', Color(0xFF2AABEE)),
    'org.thoughtcrime.securesms': Brand('Signal', Color(0xFF3A76F0)),
    'com.instagram.android': Brand('Instagram', Color(0xFFE1306C), Color(0xFFF77737)),
    'com.facebook.orca': Brand('Messenger', Color(0xFF0084FF), Color(0xFFA334FA)),
    'com.facebook.katana': Brand('Facebook', Color(0xFF1877F2)),
    'com.google.android.gm': Brand('Gmail', Color(0xFFEA4335)),
    'com.microsoft.office.outlook': Brand('Outlook', Color(0xFF0078D4)),
    'com.discord': Brand('Discord', Color(0xFF5865F2)),
    'com.Slack': Brand('Slack', Color(0xFF4A154B), Color(0xFFE01E5A)),
    'com.microsoft.teams': Brand('Teams', Color(0xFF6264A7)),
    'com.snapchat.android': Brand('Snapchat', Color(0xFFFFFC00)),
    'com.twitter.android': Brand('X', Color(0xFFE7E9EA)),
    'com.instagram.barcelona': Brand('Threads', Color(0xFFE7E9EA)),
    'com.reddit.frontpage': Brand('Reddit', Color(0xFFFF4500)),
    'com.linkedin.android': Brand('LinkedIn', Color(0xFF0A66C2)),
    'com.google.android.youtube': Brand('YouTube', Color(0xFFFF0000)),
    'com.spotify.music': Brand('Spotify', Color(0xFF1DB954)),
    'com.google.android.apps.messaging': Brand('Messages', Color(0xFF1A73E8)),
    'com.google.android.dialer': Brand('Phone', Color(0xFF34A853)),
    'com.samsung.android.messaging': Brand('Messages', Color(0xFF3E91FF)),
    'com.google.android.calendar': Brand('Calendar', Color(0xFF4285F4)),
    'com.google.android.keep': Brand('Keep', Color(0xFFFBBC04)),
    'com.amazon.mShop.android.shopping': Brand('Amazon', Color(0xFFFF9900)),
    'com.ubercab': Brand('Uber', Color(0xFFE7E9EA)),
    'com.tinder': Brand('Tinder', Color(0xFFFE3C72)),
    'com.zhiliaoapp.musically': Brand('TikTok', Color(0xFF25F4EE), Color(0xFFFE2C55)),
    'ch.sbb.mobile.android.b2c': Brand('SBB', Color(0xFFEB0000)),
    'ch.twint.payment': Brand('TWINT', Color(0xFFE7E9EA)),
  };

  static Brand? of(String? package) => _known[package];
}
