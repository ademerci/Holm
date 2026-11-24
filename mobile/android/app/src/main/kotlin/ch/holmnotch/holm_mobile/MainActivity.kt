package ch.holmnotch.holm_mobile

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.wifi.WifiManager
import android.os.Bundle
import android.provider.Settings
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    // Many phones drop broadcast packets to save power; Holm apps find each other with one (UDP port 47811).
    private var multicastLock: WifiManager.MulticastLock? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        updateFrom(intent)
        shareFrom(intent)
        // For the "new version" notification; asked once.
        val prefs = getSharedPreferences("holm_updates", MODE_PRIVATE)
        if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED &&
            !prefs.getBoolean("askedNotifications", false)) {
            prefs.edit().putBoolean("askedNotifications", true).apply()
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 8)
        }
        val wifi = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        multicastLock = wifi.createMulticastLock("holm-nearby").apply {
            setReferenceCounted(false)
            acquire()
        }
    }

    // One engine for the app, kept when the activity closes: Holm keeps running for the notch (NotchBridge).
    override fun provideFlutterEngine(context: Context): FlutterEngine = NotchBridge.mainEngine(context)

    override fun shouldDestroyEngineWithHost(): Boolean = false

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        // Plugins are registered when the engine is made; only the system screens are added here.
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "holm/system").setMethodCallHandler { call, result ->
            when (call.method) {
                "openAccessibility" -> {
                    startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                    result.success(null)
                }
                "openNotificationAccess" -> {
                    startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                    result.success(null)
                }
                "bluetoothAllowed" -> result.success(
                    checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED)
                "hasPermission" -> result.success(permissionFor(call.arguments as? String)?.let {
                    checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED } ?: false)
                "askPermission" -> {
                    val permission = permissionFor(call.arguments as? String)
                    when {
                        permission == null -> result.success(false)
                        checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED -> result.success(true)
                        else -> {
                            pendingPermission = result
                            requestPermissions(arrayOf(permission), 9)
                        }
                    }
                }
                "askBluetooth" -> {
                    requestPermissions(arrayOf(Manifest.permission.BLUETOOTH_CONNECT), 7)
                    result.success(null)
                }
                else -> result.notImplemented()
            }
        }
    }

    private var pendingPermission: MethodChannel.Result? = null

    private fun permissionFor(which: String?) = when (which) {
        "images" -> Manifest.permission.READ_MEDIA_IMAGES
        "calendar" -> Manifest.permission.READ_CALENDAR
        "calls" -> Manifest.permission.ANSWER_PHONE_CALLS
        else -> null
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == 9) {
            pendingPermission?.success(grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED)
            pendingPermission = null
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        updateFrom(intent)
        shareFrom(intent)
    }

    /**
     * Shared from another app (Android's share menu): text or files for the app to send to a PC. Kept in the bridge
     * until the app takes it, as the app may only be starting.
     */
    private fun shareFrom(intent: Intent?) {
        val action = intent?.action
        if (action != Intent.ACTION_SEND && action != Intent.ACTION_SEND_MULTIPLE) return
        val uris = if (action == Intent.ACTION_SEND) listOfNotNull(intent.getParcelableExtra(Intent.EXTRA_STREAM, android.net.Uri::class.java))
        else intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM, android.net.Uri::class.java) ?: emptyList()
        val text = intent.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString()
        if (uris.isEmpty() && text.isNullOrBlank()) return
        NotchBridge.shared = mapOf("text" to if (uris.isEmpty()) text else null, "uris" to uris.map { it.toString() })
        intent.action = Intent.ACTION_MAIN // taken once, not again when the activity comes back
        NotchBridge.toMain("shared", null)
    }

    /** "Update" in the update notification: the app installs it. */
    private fun updateFrom(intent: Intent?) {
        if (intent?.getBooleanExtra(Updates.EXTRA_UPDATE, false) != true) return
        intent.removeExtra(Updates.EXTRA_UPDATE)
        NotchBridge.toMain("installUpdate", null)
    }

    override fun onDestroy() {
        multicastLock?.release()
        super.onDestroy()
    }
}
