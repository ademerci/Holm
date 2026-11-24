package ch.holmnotch.holm_mobile

import android.Manifest
import android.accessibilityservice.AccessibilityService
import android.app.ActivityOptions
import android.app.PendingIntent
import android.bluetooth.BluetoothDevice
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.graphics.PixelFormat
import android.graphics.Rect
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.app.AlarmManager
import android.database.ContentObserver
import android.hardware.camera2.CameraManager
import android.media.AudioManager
import android.media.AudioRecordingConfiguration
import android.net.Uri
import android.os.BatteryManager
import android.os.Handler
import android.os.SystemClock
import android.provider.CalendarContract
import android.provider.MediaStore
import android.view.Gravity
import android.view.KeyEvent
import android.view.inputmethod.InputMethodManager
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.view.accessibility.AccessibilityEvent
import android.widget.FrameLayout
import io.flutter.embedding.android.FlutterSurfaceView
import io.flutter.embedding.android.FlutterView
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel

/**
 * The Dynamic Notch: an accessibility overlay over the camera, drawn by its own Flutter engine (lib/notch). An
 * accessibility overlay may lie over the status bar and the display cutout, which an ordinary overlay may not.
 * The window is only as big as the notch needs: 1×1 and untouchable while nothing shows, so the phone works as usual.
 */
class NotchService : AccessibilityService() {
    private var engine: FlutterEngine? = null
    private var root: FrameLayout? = null
    private var flutterView: FlutterView? = null
    private lateinit var params: WindowManager.LayoutParams
    private val receivers = mutableListOf<BroadcastReceiver>()
    private val callbacks = mutableListOf<ConnectivityManager.NetworkCallback>()
    private var started = 0L
    /** Where touches count, in drawing-window pixels. */
    private val touchRect = Rect()
    private var touch: View? = null
    private lateinit var touchParams: WindowManager.LayoutParams

    /** The app in front, for "camera in use by …". */
    private var foreground: String? = null
    private var cameraInUse = mutableSetOf<String>()
    private var micInUse = false
    private var torchOn = false
    private var torchCamera: String? = null
    private var screenshots: ContentObserver? = null
    private val cameraCallback = object : CameraManager.AvailabilityCallback() {
        override fun onCameraUnavailable(cameraId: String) { if (cameraInUse.add(cameraId)) sendPrivacy() }
        override fun onCameraAvailable(cameraId: String) { if (cameraInUse.remove(cameraId)) sendPrivacy() }
    }
    private val torchCallback = object : CameraManager.TorchCallback() {
        override fun onTorchModeChanged(cameraId: String, enabled: Boolean) {
            torchCamera = cameraId
            torchOn = enabled
            NotchBridge.toNotch(mapOf("type" to "torch", "on" to enabled)) // the overview's flashlight button
        }
    }
    private val micCallback = object : AudioManager.AudioRecordingCallback() {
        override fun onRecordingConfigChanged(configs: MutableList<AudioRecordingConfiguration>) {
            val on = configs.isNotEmpty()
            if (on != micInUse) {
                micInUse = on
                sendPrivacy()
            }
        }
    }

    private val wm get() = getSystemService(WINDOW_SERVICE) as WindowManager
    private val density get() = resources.displayMetrics.density

    override fun onServiceConnected() {
        super.onServiceConnected()
        NotchBridge.service = this
        NotchBridge.mainEngine(this) // Holm keeps running in the background, for its events in the notch
        val e = NotchBridge.notchEngine(this)
        engine = e
        MethodChannel(e.dartExecutor.binaryMessenger, "holm/notch").setMethodCallHandler(::onCall)

        // A surface that lets the screen show through around the notch.
        val view = FlutterView(this, FlutterSurfaceView(this, true))
        flutterView = view
        val frame = FrameLayout(this)
        frame.addView(view, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        root = frame
        params = WindowManager.LayoutParams(
            1, 1,
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            // The drawing window never takes touches itself: the touch window over the notch hands them on.
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS or
                WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED,
            PixelFormat.TRANSLUCENT,
        ).apply {
            gravity = Gravity.TOP or Gravity.CENTER_HORIZONTAL
            layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS
            // From the very top of the screen, over the status bar and the camera: by default Android keeps windows
            // clear of the system bars, which put the notch under the status bar on phones with a camera cut-out.
            fitInsetsTypes = 0
            fitInsetsSides = 0
            title = "Holm notch"
        }
        wm.addView(frame, params)
        touch = touchWindow()
        view.attachToFlutterEngine(e)
        e.lifecycleChannel.appIsResumed()
        started = SystemClock.elapsedRealtime()
        watch()
        NotchBridge.applyPopups(this, notchRunning = true)
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        if (event?.eventType == AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) {
            val pkg = event.packageName?.toString() ?: return
            if (pkg != packageName && pkg != "com.android.systemui") foreground = pkg
        }
        if (event?.eventType == AccessibilityEvent.TYPE_WINDOWS_CHANGED || event?.eventType == AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) {
            followShade()
        }
    }

    /** Android's notification shade or quick settings is pulled down: the notch steps aside, it doesn't cover them. */
    private var shadeOpen = false
    private var lastWindow = Triple(0.0, 0.0, false)

    private fun followShade() {
        val open = shadeShown()
        if (open == shadeOpen) return
        shadeOpen = open
        // Hidden, not faded: the notch draws on a surface of its own, which a parent's alpha doesn't reach.
        root?.visibility = if (open) View.INVISIBLE else View.VISIBLE
        android.util.Log.d("HolmNotch", "shade " + (if (open) "open: notch hidden" else "closed: notch back"))
        if (open) NotchBridge.toNotch(mapOf("type" to "outside")) // an open notch closes
        val (w, h, t) = lastWindow
        setWindow(w, h, t) // and takes no touches meanwhile
    }

    /**
     * The shade is a system window across the screen and at least a third of its height (the status bar alone is a
     * strip, the volume panel narrow). On the lock screen the same window is the lock screen itself: there the notch
     * stays.
     */
    private fun shadeShown(): Boolean {
        val keyguard = getSystemService(android.app.KeyguardManager::class.java)
        if (keyguard?.isKeyguardLocked == true) return false
        val screen = wm.currentWindowMetrics.bounds
        val list = runCatching { windows }.getOrNull() ?: return false
        val bounds = Rect()
        return list.any { win ->
            if (win.type != android.view.accessibility.AccessibilityWindowInfo.TYPE_SYSTEM) return@any false
            win.getBoundsInScreen(bounds)
            bounds.width() >= screen.width() * 0.8 && bounds.height() >= screen.height() / 3
        }
    }

    /**
     * The volume keys, while "Volume" is on in the notch's settings: the notch shows the level, not Android's slider.
     * In a call they set the call's volume, otherwise the media's.
     */
    /** Holding a volume key: Android doesn't repeat a key the service keeps for itself, so the notch does. */
    private var volumeRepeat: Runnable? = null

    override fun onKeyEvent(event: KeyEvent): Boolean {
        if (event.keyCode != KeyEvent.KEYCODE_VOLUME_UP && event.keyCode != KeyEvent.KEYCODE_VOLUME_DOWN) return false
        if (!volumeInNotch()) return false
        val up = event.keyCode == KeyEvent.KEYCODE_VOLUME_UP
        when (event.action) {
            KeyEvent.ACTION_DOWN -> {
                if (event.repeatCount > 0) return true // ours repeats instead
                stepVolume(up)
                volumeRepeat?.let { NotchBridge.handler.removeCallbacks(it) }
                val repeat = object : Runnable {
                    override fun run() {
                        stepVolume(up)
                        NotchBridge.handler.postDelayed(this, 90)
                    }
                }
                volumeRepeat = repeat
                NotchBridge.handler.postDelayed(repeat, 400)
            }
            KeyEvent.ACTION_UP -> {
                volumeRepeat?.let { NotchBridge.handler.removeCallbacks(it) }
                volumeRepeat = null
            }
        }
        return true
    }

    /** The stream the keys set: the call's while talking, else the media's. */
    private fun keyStream(am: AudioManager): Int {
        val call = am.mode == AudioManager.MODE_IN_CALL || am.mode == AudioManager.MODE_IN_COMMUNICATION
        return if (call) AudioManager.STREAM_VOICE_CALL else AudioManager.STREAM_MUSIC
    }

    private fun stepVolume(up: Boolean) {
        val am = getSystemService(AudioManager::class.java)
        val stream = keyStream(am)
        runCatching { am.adjustStreamVolume(stream, if (up) AudioManager.ADJUST_RAISE else AudioManager.ADJUST_LOWER, 0) }
        sendVolume(am, stream)
    }

    private fun sendVolume(am: AudioManager, stream: Int) {
        NotchBridge.toNotch(
            mapOf("type" to "volume", "level" to level(am, stream), "call" to (stream == AudioManager.STREAM_VOICE_CALL))
        )
    }

    private fun level(am: AudioManager, stream: Int): Double {
        val max = am.getStreamMaxVolume(stream)
        val min = am.getStreamMinVolume(stream)
        return (am.getStreamVolume(stream) - min).toDouble() / (max - min).coerceAtLeast(1)
    }

    private val streams = mapOf(
        "media" to AudioManager.STREAM_MUSIC,
        "ring" to AudioManager.STREAM_RING,
        "notification" to AudioManager.STREAM_NOTIFICATION,
        "alarm" to AudioManager.STREAM_ALARM,
        "call" to AudioManager.STREAM_VOICE_CALL,
    )

    /** Every stream's level (0..1), for the open volume notch. */
    private fun volumes(): Map<String, Double> {
        val am = getSystemService(AudioManager::class.java)
        return streams.mapValues { (_, stream) -> level(am, stream) }
    }

    /** Sets a stream ("media", "ring", …, or "keys" for the one the keys set) to [value] (0..1). */
    private fun setVolume(which: String?, value: Double) {
        val am = getSystemService(AudioManager::class.java)
        val stream = if (which == "keys") keyStream(am) else streams[which] ?: return
        val min = am.getStreamMinVolume(stream)
        val max = am.getStreamMaxVolume(stream)
        // Ringtone and notifications can't be set while Do Not Disturb holds them (Android refuses): left as they are.
        runCatching { am.setStreamVolume(stream, min + Math.round(value.coerceIn(0.0, 1.0) * (max - min)).toInt(), 0) }
        if (which == "keys") sendVolume(am, stream)
    }

    private fun volumeInNotch(): Boolean {
        val json = NotchBridge.settings(this) ?: return true
        return runCatching {
            val off = org.json.JSONObject(json).optJSONArray("off") ?: return true
            (0 until off.length()).none { off.optString(it) == "volume" }
        }.getOrDefault(true)
    }

    override fun onInterrupt() {}

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        NotchBridge.toNotch(mapOf("type" to "orientation", "landscape" to (newConfig.orientation == Configuration.ORIENTATION_LANDSCAPE)))
    }

    override fun onDestroy() {
        runCatching { getSystemService(CameraManager::class.java).unregisterAvailabilityCallback(cameraCallback) }
        runCatching { getSystemService(CameraManager::class.java).unregisterTorchCallback(torchCallback) }
        runCatching { getSystemService(AudioManager::class.java).unregisterAudioRecordingCallback(micCallback) }
        screenshots?.let { runCatching { contentResolver.unregisterContentObserver(it) } }
        for (r in receivers) runCatching { unregisterReceiver(r) }
        val cm = getSystemService(ConnectivityManager::class.java)
        for (c in callbacks) runCatching { cm.unregisterNetworkCallback(c) }
        NotchBridge.notch = null
        NotchBridge.service = null
        NotchBridge.applyPopups(this, notchRunning = false) // Android's pop-ups come back with the notch gone
        flutterView?.detachFromFlutterEngine()
        root?.let { runCatching { wm.removeView(it) } }
        touch?.let { runCatching { wm.removeView(it) } }
        engine?.destroy()
        super.onDestroy()
    }

    private fun onCall(call: MethodCall, result: MethodChannel.Result) {
        when (call.method) {
            "ready" -> {
                NotchBridge.notch = MethodChannel(engine!!.dartExecutor.binaryMessenger, "holm/notch").also {
                    it.setMethodCallHandler(::onCall)
                }
                result.success(
                    mapOf(
                        "settings" to NotchBridge.settings(this),
                        "landscape" to (resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE),
                        "touchRegion" to true,
                    )
                )
                NotchBridge.listener?.resendAll()
                sendBattery(alert = false)
            }
            "window" -> {
                val w = (call.argument<Double>("w") ?: 0.0)
                val h = (call.argument<Double>("h") ?: 0.0)
                val touchable = call.argument<Boolean>("touchable") ?: false
                val r = call.argument<List<Double>>("rect")
                if (r != null) {
                    touchRect.set((r[0] * density).toInt(), (r[1] * density).toInt(), ((r[0] + r[2]) * density).toInt(), ((r[1] + r[3]) * density).toInt())
                } else touchRect.setEmpty()
                setWindow(w, h, touchable)
                result.success(null)
            }
            "media" -> {
                NotchBridge.listener?.mediaAction(call.argument<String>("action") ?: "", call.argument<Number>("position")?.toLong())
                result.success(null)
            }
            "open" -> {
                val key = call.argument<String>("key")
                val intent = key?.let { NotchBridge.listener?.contentIntent(it) }
                if (intent != null) send(intent) else openApp(call.argument<String>("page"))
                result.success(null)
            }
            "openApp" -> {
                openApp(call.argument<String>("page"))
                result.success(null)
            }
            "notificationAction" -> {
                val key = call.argument<String>("key")
                val index = call.argument<Int>("index")
                if (key != null && index != null) NotchBridge.listener?.fireAction(key, index)
                result.success(null)
            }
            "shade" -> {
                performGlobalAction(GLOBAL_ACTION_NOTIFICATIONS)
                result.success(null)
            }
            "reply" -> result.success(
                NotchBridge.listener?.reply(call.argument<String>("key") ?: "", call.argument<Int>("index") ?: -1, call.argument<String>("text") ?: "")
                    ?: false
            )
            "clear" -> {
                call.argument<String>("key")?.let { NotchBridge.listener?.clear(it) }
                result.success(null)
            }
            "call" -> {
                val key = call.argument<String>("key")
                if (key != null) NotchBridge.listener?.callAction(key, call.argument<String>("action") ?: "")
                result.success(null)
            }
            "muteCall" -> {
                val am = getSystemService(AudioManager::class.java)
                am.isMicrophoneMute = !am.isMicrophoneMute
                result.success(am.isMicrophoneMute)
            }
            "keyboard" -> {
                keyboard(call.arguments == true)
                result.success(null)
            }
            "gesture" -> {
                gesture(call.arguments as? String)
                result.success(null)
            }
            "phoneStatus" -> {
                NotchBridge.toMain("phoneStatus", call.arguments) // the app sends it to the account's PCs
                result.success(null)
            }
            "beamScreenshot" -> {
                NotchBridge.toMain("beamFile", call.arguments)
                result.success(null)
            }
            "overview" -> result.success(overview())
            "volumes" -> result.success(volumes())
            "setVolume" -> {
                setVolume(call.argument<String>("stream"), call.argument<Double>("level") ?: 0.0)
                result.success(volumes())
            }
            "offerAnswer" -> {
                NotchBridge.toMain("offerAnswer", call.arguments)
                result.success(null)
            }
            "mute" -> {
                NotchBridge.toMain("mute", call.arguments) // the app keeps the settings
                result.success(null)
            }
            else -> result.notImplemented()
        }
    }

    /**
     * Typing a reply: the notch's window takes the keyboard's focus while the text field is open, and gives it back
     * after, so the app underneath gets its keys again.
     */
    private fun keyboard(on: Boolean) {
        val frame = root ?: return
        params.flags = if (on) params.flags and WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE.inv()
        else params.flags or WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
        params.softInputMode = WindowManager.LayoutParams.SOFT_INPUT_ADJUST_NOTHING
        runCatching { wm.updateViewLayout(frame, params) }
        if (on) flutterView?.requestFocus()
        else flutterView?.let { getSystemService(InputMethodManager::class.java).hideSoftInputFromWindow(it.windowToken, 0) }
    }

    /** A double tap or a long press on the closed notch (lib/notch/settings.dart, NotchGesture). */
    private fun gesture(what: String?) {
        when (what) {
            "torch" -> runCatching {
                val cm = getSystemService(CameraManager::class.java)
                val id = torchCamera ?: cm.cameraIdList.firstOrNull {
                    cm.getCameraCharacteristics(it).get(android.hardware.camera2.CameraCharacteristics.FLASH_INFO_AVAILABLE) == true
                } ?: return
                cm.setTorchMode(id, !torchOn)
            }
            "playPause" -> NotchBridge.listener?.mediaAction("toggle", null)
            "screenshot" -> performGlobalAction(GLOBAL_ACTION_TAKE_SCREENSHOT)
            "holm" -> openApp(null)
            "note" -> openApp("notes")
        }
    }

    /** What the overview shows besides the time: the next alarm and the next event (if Holm may read the calendar). */
    private fun overview(): Map<String, Any?> {
        val alarm = getSystemService(AlarmManager::class.java).nextAlarmClock?.triggerTime
        var event: Map<String, Any?>? = null
        if (checkSelfPermission(Manifest.permission.READ_CALENDAR) == PackageManager.PERMISSION_GRANTED) {
            runCatching {
                val now = System.currentTimeMillis()
                val uri = CalendarContract.Instances.CONTENT_URI.buildUpon().also {
                    android.content.ContentUris.appendId(it, now)
                    android.content.ContentUris.appendId(it, now + 24 * 3600_000L)
                }.build()
                contentResolver.query(
                    uri, arrayOf(CalendarContract.Instances.TITLE, CalendarContract.Instances.BEGIN, CalendarContract.Instances.ALL_DAY),
                    "${CalendarContract.Instances.BEGIN} >= ? AND ${CalendarContract.Instances.ALL_DAY} = 0", arrayOf(now.toString()),
                    "${CalendarContract.Instances.BEGIN} ASC",
                )?.use { c -> if (c.moveToFirst()) event = mapOf("title" to c.getString(0), "begin" to c.getLong(1)) }
            }
        }
        return mapOf("alarm" to alarm, "event" to event)
    }

    private fun setWindow(w: Double, h: Double, touchable: Boolean) {
        lastWindow = Triple(w, h, touchable)
        val frame = root ?: return
        val width = if (w <= 0) 1 else (w * density).toInt()
        val height = if (h <= 0) 1 else (h * density).toInt()
        if (params.width != width || params.height != height) {
            params.width = width
            params.height = height
            runCatching { wm.updateViewLayout(frame, params) }
        }
        // Without a rect (an older notch engine), the touch window covers the drawing window.
        val area = if (touchRect.isEmpty) Rect(0, 0, width, height) else touchRect
        val on = touchable && !shadeOpen && w > 0 && !area.isEmpty
        val t = touch ?: return
        var flags = touchParams.flags
        flags = if (on) flags and WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE.inv() else flags or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
        val x = if (on) drawLeft() + area.left else 0
        val y = if (on) area.top else 0
        val tw = if (on) area.width() else 1
        val th = if (on) area.height() else 1
        if (touchParams.flags == flags && touchParams.x == x && touchParams.y == y && touchParams.width == tw && touchParams.height == th) return
        touchParams.flags = flags
        touchParams.x = x
        touchParams.y = y
        touchParams.width = tw
        touchParams.height = th
        runCatching { wm.updateViewLayout(t, touchParams) }
    }

    /**
     * The touch window: invisible, exactly over the notch (all of the drawing window while it is open), handing its
     * touches to the notch. It may change size every frame: it has nothing to draw, so nothing jumps.
     */
    private fun touchWindow(): View {
        val v = object : View(this) {
            @android.annotation.SuppressLint("ClickableViewAccessibility")
            override fun onTouchEvent(ev: MotionEvent): Boolean {
                if (ev.actionMasked == MotionEvent.ACTION_OUTSIDE) {
                    NotchBridge.toNotch(mapOf("type" to "outside"))
                    return false
                }
                val copy = MotionEvent.obtain(ev)
                copy.offsetLocation((touchParams.x - drawLeft()).toFloat(), touchParams.y.toFloat())
                flutterView?.dispatchTouchEvent(copy)
                copy.recycle()
                return true
            }
        }
        touchParams = WindowManager.LayoutParams(
            1, 1,
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS or
                WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH,
            PixelFormat.TRANSPARENT,
        ).apply {
            gravity = Gravity.TOP or Gravity.LEFT
            layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS
            fitInsetsTypes = 0
            fitInsetsSides = 0
            title = "Holm notch touch"
        }
        wm.addView(v, touchParams)
        return v
    }

    /** Where the drawing window starts on the screen (it is centred). */
    private fun drawLeft(): Int = (wm.currentWindowMetrics.bounds.width() - params.width) / 2

    private fun send(intent: PendingIntent) {
        // The service is bound by the system: it may start the app the notification belongs to.
        val options = ActivityOptions.makeBasic()
            .setPendingIntentBackgroundActivityStartMode(ActivityOptions.MODE_BACKGROUND_ACTIVITY_START_ALLOWED)
        runCatching { intent.send(this, 0, null, null, null, null, options.toBundle()) }
    }

    private fun openApp(page: String?) {
        val intent = Intent(this, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        if (page != null) {
            intent.putExtra("page", page)
            NotchBridge.toMain("open", page)
        }
        runCatching { startActivity(intent) }
    }

    // ───────────── system sources ─────────────

    /** True for the first moments after starting: the state then is what was there, not news to show. */
    private val settling get() = SystemClock.elapsedRealtime() - started < 2500

    private fun watch() {
        receive(IntentFilter().apply {
            addAction(Intent.ACTION_BATTERY_CHANGED)
            addAction(Intent.ACTION_POWER_CONNECTED)
            addAction(Intent.ACTION_POWER_DISCONNECTED)
        }) { intent ->
            when (intent.action) {
                Intent.ACTION_POWER_CONNECTED, Intent.ACTION_POWER_DISCONNECTED ->
                    NotchBridge.handler.postDelayed({ sendBattery(alert = true) }, 400)
                else -> sendBattery(alert = false)
            }
        }
        receive(IntentFilter().apply {
            addAction(BluetoothDevice.ACTION_ACL_CONNECTED)
            addAction(BluetoothDevice.ACTION_ACL_DISCONNECTED)
        }) { intent ->
            val device = intent.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE, BluetoothDevice::class.java)
            val name = if (checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED)
                runCatching { device?.alias ?: device?.name }.getOrNull() else null
            // Headphones (audio), not a watch or a car's phone link: they get the earbuds activity.
            val audio = runCatching {
                device?.bluetoothClass?.majorDeviceClass == android.bluetooth.BluetoothClass.Device.Major.AUDIO_VIDEO
            }.getOrDefault(false)
            NotchBridge.toNotch(
                mapOf("type" to "bluetooth", "connected" to (intent.action == BluetoothDevice.ACTION_ACL_CONNECTED), "name" to name, "audio" to audio)
            )
        }
        // Headphones say how full they are a moment after connecting (and when it changes).
        receive(IntentFilter(ACTION_BATTERY_LEVEL)) { intent ->
            val level = intent.getIntExtra(EXTRA_BATTERY_LEVEL, -1)
            if (level < 0) return@receive
            val device = intent.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE, BluetoothDevice::class.java)
            val name = if (checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED)
                runCatching { device?.alias ?: device?.name }.getOrNull() else null
            NotchBridge.toNotch(mapOf("type" to "bluetoothBattery", "name" to name, "level" to level))
        }
        // The volume, however it changed (keys the service didn't get, a slider, another app): the notch shows it.
        receive(IntentFilter(ACTION_VOLUME_CHANGED)) { intent ->
            val stream = intent.getIntExtra(EXTRA_VOLUME_STREAM, -1)
            if (stream != AudioManager.STREAM_MUSIC && stream != AudioManager.STREAM_VOICE_CALL) return@receive
            if (!volumeInNotch() || settling) return@receive
            val am = getSystemService(AudioManager::class.java)
            val min = am.getStreamMinVolume(stream)
            val max = am.getStreamMaxVolume(stream)
            NotchBridge.toNotch(
                mapOf(
                    "type" to "volume",
                    "level" to (am.getStreamVolume(stream) - min).toDouble() / (max - min).coerceAtLeast(1),
                    "call" to (stream == AudioManager.STREAM_VOICE_CALL),
                )
            )
        }
        // Camera and microphone in use, by any app.
        runCatching { getSystemService(CameraManager::class.java).registerAvailabilityCallback(cameraCallback, NotchBridge.handler) }
        runCatching { getSystemService(CameraManager::class.java).registerTorchCallback(torchCallback, NotchBridge.handler) }
        runCatching { getSystemService(AudioManager::class.java).registerAudioRecordingCallback(micCallback, NotchBridge.handler) }
        watchScreenshots()

        val cm = getSystemService(ConnectivityManager::class.java)
        network(cm, NetworkRequest.Builder().addTransportType(NetworkCapabilities.TRANSPORT_WIFI).build()) { up ->
            mapOf("type" to "wifi", "connected" to up)
        }
        network(
            cm,
            NetworkRequest.Builder().addTransportType(NetworkCapabilities.TRANSPORT_VPN)
                .removeCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN).build()
        ) { up ->
            mapOf("type" to "vpn", "active" to up, "mullvad" to isMullvad())
        }
    }

    private fun sendPrivacy() {
        // The camera Holm's own torch uses doesn't count.
        val camera = cameraInUse.any { it != torchCamera || !torchOn }
        val app = foreground?.let { pkg ->
            runCatching { packageManager.getApplicationLabel(packageManager.getApplicationInfo(pkg, 0)).toString() }.getOrNull()
        }
        NotchBridge.toNotch(mapOf("type" to "privacy", "camera" to camera, "mic" to micInUse, "app" to app, "package" to foreground))
    }

    /** A new screenshot: the notch offers to send it to the PC (if Holm may see the pictures). */
    private fun watchScreenshots() {
        val observer = object : ContentObserver(Handler(mainLooper)) {
            private var last: Uri? = null
            override fun onChange(selfChange: Boolean, uri: Uri?) {
                if (checkSelfPermission(Manifest.permission.READ_MEDIA_IMAGES) != PackageManager.PERMISSION_GRANTED) return
                runCatching {
                    contentResolver.query(
                        MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
                        arrayOf(MediaStore.Images.Media._ID, MediaStore.Images.Media.RELATIVE_PATH, MediaStore.Images.Media.DATE_ADDED,
                            MediaStore.Images.Media.DISPLAY_NAME, MediaStore.Images.Media.IS_PENDING),
                        null, null, "${MediaStore.Images.Media.DATE_ADDED} DESC",
                    )?.use { c ->
                        if (!c.moveToFirst()) return
                        val path = c.getString(1) ?: ""
                        val added = c.getLong(2)
                        if (c.getInt(4) != 0 || !path.contains("Screenshot", ignoreCase = true)) return
                        if (System.currentTimeMillis() / 1000 - added > 15) return
                        val item = android.content.ContentUris.withAppendedId(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, c.getLong(0))
                        if (item == last) return
                        last = item
                        val thumb = runCatching {
                            contentResolver.loadThumbnail(item, android.util.Size(160, 160), null).let { NotchListener.png(it, 160) }
                        }.getOrNull()
                        NotchBridge.toNotch(mapOf("type" to "screenshot", "uri" to item.toString(), "name" to c.getString(3), "thumb" to thumb))
                    }
                }
            }
        }
        contentResolver.registerContentObserver(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, true, observer)
        screenshots = observer
    }

    private fun receive(filter: IntentFilter, onReceive: (Intent) -> Unit) {
        val r = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) = onReceive(intent)
        }
        registerReceiver(r, filter, RECEIVER_EXPORTED)
        receivers += r
    }

    private fun network(cm: ConnectivityManager, request: NetworkRequest, event: (Boolean) -> Map<String, Any?>) {
        val live = mutableSetOf<Network>()
        val cb = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                val was = live.isNotEmpty()
                live += network
                if (!was) NotchBridge.toNotch(event(true) + ("alert" to !settling))
            }

            override fun onLost(network: Network) {
                live -= network
                if (live.isEmpty()) NotchBridge.toNotch(event(false) + ("alert" to !settling))
            }
        }
        cm.registerNetworkCallback(request, cb)
        callbacks += cb
    }

    /** Mullvad VPN shows its own notification while it is connected; its app being there is the next best hint. */
    private fun isMullvad(): Boolean {
        if (NotchBridge.listener?.hasNotificationFrom(MULLVAD) == true) return true
        return runCatching { packageManager.getPackageInfo(MULLVAD, 0); true }.getOrDefault(false)
    }

    private fun sendBattery(alert: Boolean) {
        val intent = registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED)) ?: return
        val level = intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
        val scale = intent.getIntExtra(BatteryManager.EXTRA_SCALE, 100)
        val plugged = intent.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0
        val status = intent.getIntExtra(BatteryManager.EXTRA_STATUS, 0)
        NotchBridge.toNotch(
            mapOf(
                "type" to "battery",
                "level" to if (level < 0) null else level * 100 / scale,
                "plugged" to plugged,
                "full" to (status == BatteryManager.BATTERY_STATUS_FULL),
                "alert" to alert,
            )
        )
    }

    companion object {
        const val MULLVAD = "net.mullvad.mullvadvpn"
        /** BluetoothDevice.ACTION_BATTERY_LEVEL_CHANGED and its extra (hidden from the SDK, sent to everyone). */
        const val ACTION_BATTERY_LEVEL = "android.bluetooth.device.action.BATTERY_LEVEL_CHANGED"
        const val EXTRA_BATTERY_LEVEL = "android.bluetooth.device.extra.BATTERY_LEVEL"
        /** AudioManager.VOLUME_CHANGED_ACTION and its stream extra (hidden from the SDK, sent to everyone). */
        const val ACTION_VOLUME_CHANGED = "android.media.VOLUME_CHANGED_ACTION"
        const val EXTRA_VOLUME_STREAM = "android.media.EXTRA_VOLUME_STREAM_TYPE"
    }
}
