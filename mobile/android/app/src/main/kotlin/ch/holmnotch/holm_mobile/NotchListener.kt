package ch.holmnotch.holm_mobile

import android.app.Notification
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.RemoteInput
import android.content.Intent
import android.os.Bundle
import android.content.ComponentName
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.drawable.Drawable
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import java.io.ByteArrayOutputStream

/**
 * What the notch hears from the system's notifications: new ones, ongoing ones (downloads, timers, Live Updates, Quick
 * Share) and the media session that plays (Spotify or any player), with its controls. Classifying is up to the Dart side
 * (lib/notch/activities.dart); this only describes.
 */
class NotchListener : NotificationListenerService() {
    private var sessions: MediaSessionManager? = null
    private var controller: MediaController? = null
    private val iconsSent = mutableSetOf<String>()
    private var lastArtKey: String? = null
    /** Covers loaded from a player's link (Spotify gives one instead of a picture), by song. */
    private val artFromUri = object : LinkedHashMap<String, ByteArray>(8, 0.75f, true) {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, ByteArray>?) = size > 8
    }
    private val artLoading = mutableSetOf<String>()
    /** The song whose cover is still missing, and how often it was looked for again. */
    private var artMissingKey: String? = null
    private var artRetries = 0
    private val me get() = ComponentName(this, NotchListener::class.java)

    private val onSessions = MediaSessionManager.OnActiveSessionsChangedListener { list -> pick(list ?: emptyList()) }

    private val onMedia = object : MediaController.Callback() {
        override fun onMetadataChanged(metadata: MediaMetadata?) = sendMedia()
        override fun onPlaybackStateChanged(state: PlaybackState?) {
            // Another session may have started playing meanwhile: follow the one that plays.
            sessions?.let { pick(it.getActiveSessions(me)) } ?: sendMedia()
        }
        override fun onSessionDestroyed() {
            sessions?.let { pick(it.getActiveSessions(me)) }
        }
    }

    override fun onListenerConnected() {
        super.onListenerConnected()
        NotchBridge.listener = this
        val msm = getSystemService(MediaSessionManager::class.java)
        sessions = msm
        runCatching {
            msm.addOnActiveSessionsChangedListener(onSessions, me)
            pick(msm.getActiveSessions(me))
        }
        resendAll()
    }

    override fun onInterruptionFilterChanged(interruptionFilter: Int) = sendDnd()

    private fun sendDnd() {
        val filter = runCatching { currentInterruptionFilter }.getOrDefault(INTERRUPTION_FILTER_ALL)
        NotchBridge.toNotch(mapOf("type" to "dnd", "on" to (filter != INTERRUPTION_FILTER_ALL && filter != INTERRUPTION_FILTER_UNKNOWN)))
    }

    override fun onListenerDisconnected() {
        sessions?.removeOnActiveSessionsChangedListener(onSessions)
        controller?.unregisterCallback(onMedia)
        controller = null
        if (NotchBridge.listener === this) NotchBridge.listener = null
        super.onListenerDisconnected()
    }

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        send(sbn, initial = false)
        // A player's notification may bring the cover its session lacks.
        // (Also the first sign of a new player: look at the sessions again.)
        if (sbn.notification.extras.containsKey(Notification.EXTRA_MEDIA_SESSION)) {
            sessions?.let { pick(runCatching { it.getActiveSessions(me) }.getOrDefault(emptyList())) } ?: sendMedia()
        }
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification) {
        NotchBridge.toNotch(mapOf("type" to "notificationRemoved", "key" to sbn.key))
    }

    /** Everything as it is now, without popping anything out (the notch just started). */
    fun resendAll() {
        runCatching { activeNotifications }.getOrNull()?.forEach { send(it, initial = true) }
        lastArtKey = null
        sendMedia()
        sendDnd()
    }

    fun hasNotificationFrom(pkg: String) = runCatching { activeNotifications.any { it.packageName == pkg } }.getOrDefault(false)

    /** Presses one of the notification's buttons. */
    fun fireAction(key: String, index: Int) {
        val action = runCatching { activeNotifications.firstOrNull { it.key == key }?.notification?.actions?.getOrNull(index) }.getOrNull()
        runCatching { action?.actionIntent?.send() }
    }

    /** Answers a message from the notch: the text goes to the app as if typed in its notification. */
    fun reply(key: String, index: Int, text: String): Boolean = runCatching {
        val action = activeNotifications.firstOrNull { it.key == key }?.notification?.actions?.getOrNull(index) ?: return false
        val inputs = action.remoteInputs ?: return false
        val intent = Intent()
        RemoteInput.addResultsToIntent(inputs, intent, Bundle().apply { putCharSequence(inputs.first().resultKey, text) })
        action.actionIntent.send(this, 0, intent)
        true
    }.getOrDefault(false)

    /** Clears a notification from the shade too (a swipe to the right in the notch), if it can be cleared. */
    fun clear(key: String) {
        runCatching { if (activeNotifications.any { it.key == key && it.isClearable }) cancelNotification(key) }
    }

    /** Answer, decline or hang up a call, with the buttons of the phone app's call notification. */
    fun callAction(key: String, which: String) {
        // With "Answer calls" allowed, Android's telephony does it: listeners get call notifications without their
        // buttons on Android 15 and later.
        if (checkSelfPermission(android.Manifest.permission.ANSWER_PHONE_CALLS) == android.content.pm.PackageManager.PERMISSION_GRANTED) {
            val telecom = getSystemService(android.telecom.TelecomManager::class.java)
            val done = runCatching {
                @Suppress("DEPRECATION")
                if (which == "answer") telecom.acceptRingingCall() else telecom.endCall()
                true
            }.getOrDefault(false)
            if (done) return
        }
        val n = runCatching { activeNotifications.firstOrNull { it.key == key }?.notification }.getOrNull() ?: return
        val x = n.extras
        val actions = n.actions ?: emptyArray()
        // CallStyle keeps its buttons in the extras; listeners don't always get them, but the same buttons are the
        // notification's actions: Decline then Answer while it rings, Hang up while you talk.
        val intent = when (which) {
            "answer" -> x.getParcelable(Notification.EXTRA_ANSWER_INTENT, PendingIntent::class.java)
                ?: actions.lastOrNull()?.actionIntent
            "decline" -> x.getParcelable(Notification.EXTRA_DECLINE_INTENT, PendingIntent::class.java)
                ?: actions.firstOrNull()?.actionIntent
            else -> x.getParcelable(Notification.EXTRA_HANG_UP_INTENT, PendingIntent::class.java)
                ?: x.getParcelable(Notification.EXTRA_DECLINE_INTENT, PendingIntent::class.java)
                ?: actions.firstOrNull()?.actionIntent
        }
        runCatching { intent?.send() }.onFailure { android.util.Log.w("HolmNotch", "call $which failed", it) }
    }

    fun contentIntent(key: String): PendingIntent? =
        runCatching { activeNotifications.firstOrNull { it.key == key }?.notification?.contentIntent }.getOrNull()

    private fun send(sbn: StatusBarNotification, initial: Boolean) {
        if (sbn.packageName == packageName) return
        val n = sbn.notification
        val x = n.extras
        val pkg = sbn.packageName
        // Google Play services' earbuds card ("L: 100%  C: 96%  R: 100%"): the earbuds activity shows it, not a pop-up.
        if (pkg == "com.google.android.gms" && sendBudsBattery(n)) return
        sendIcon(pkg)
        val ranking = Ranking()
        val importance = if (currentRanking?.getRanking(sbn.key, ranking) == true) ranking.importance else NotificationManager.IMPORTANCE_DEFAULT
        val isMedia = x.containsKey(Notification.EXTRA_MEDIA_SESSION) ||
            x.getString(Notification.EXTRA_TEMPLATE)?.contains("MediaStyle") == true
        val callType = x.getInt(Notification.EXTRA_CALL_TYPE, 0)
        // The turn arrow of a navigation, the caller's photo: the notification's own picture.
        val picture = if (pkg in PICTURE_APPS || callType != 0 || n.category == Notification.CATEGORY_NAVIGATION)
            runCatching { n.getLargeIcon()?.loadDrawable(this)?.let { png(it, 96) } }.getOrNull() else null
        NotchBridge.toNotch(
            mapOf(
                "type" to "notification",
                "initial" to initial,
                "key" to sbn.key,
                "package" to pkg,
                "app" to label(pkg),
                "title" to x.getCharSequence(Notification.EXTRA_TITLE)?.toString(),
                "text" to (x.getCharSequence(Notification.EXTRA_TEXT) ?: x.getCharSequence(Notification.EXTRA_BIG_TEXT))?.toString(),
                "sub" to x.getCharSequence(Notification.EXTRA_SUB_TEXT)?.toString(),
                "category" to n.category,
                "channel" to n.channelId,
                // Google's Clock keeps the timer's state and time left here ("RUNNING|…|⏳0:03:31"); its view is custom.
                "sortKey" to n.sortKey,
                "ongoing" to sbn.isOngoing,
                "summary" to ((n.flags and Notification.FLAG_GROUP_SUMMARY) != 0),
                "media" to isMedia,
                "progress" to x.getInt(Notification.EXTRA_PROGRESS, 0),
                "progressMax" to x.getInt(Notification.EXTRA_PROGRESS_MAX, 0),
                "indeterminate" to x.getBoolean(Notification.EXTRA_PROGRESS_INDETERMINATE, false),
                "chronometer" to x.getBoolean(Notification.EXTRA_SHOW_CHRONOMETER, false),
                "countDown" to x.getBoolean(Notification.EXTRA_CHRONOMETER_COUNT_DOWN, false),
                "when" to n.`when`,
                // Android 16 Live Updates: promoted ongoing notifications, with a short text for the status chip.
                "promoted" to ((n.flags and FLAG_PROMOTED_ONGOING) != 0),
                "shortText" to x.getCharSequence("android.shortCriticalText")?.toString(),
                "color" to n.color,
                "importance" to importance,
                "posted" to sbn.postTime,
                // The notification's buttons ("Stop", "Add 1 min", "Mark as read"); a reply's is marked: it asks for text.
                "actions" to (n.actions ?: emptyArray()).mapIndexed { i, a ->
                    listOf(i, a.title?.toString() ?: "", a.remoteInputs?.any { it.allowFreeFormInput } == true)
                },
                // Calls: 1 incoming, 2 ongoing, 3 screening (Notification.CallStyle).
                "callType" to callType,
                "caller" to (if (callType != 0) runCatching {
                    x.getParcelable(Notification.EXTRA_CALL_PERSON, android.app.Person::class.java)?.name?.toString()
                }.getOrNull() else null),
                "picture" to picture,
            )
        )
    }

    /**
     * Each earbud's and the case's battery, from Google Play services' notification for Fast Pair earbuds. Its view is
     * custom: its texts are read from the view, laid out off screen. True if it was that card.
     */
    private fun sendBudsBattery(n: Notification): Boolean {
        val x = n.extras
        val texts = mutableListOf<String>()
        listOf(Notification.EXTRA_TITLE, Notification.EXTRA_TEXT, Notification.EXTRA_BIG_TEXT, Notification.EXTRA_SUB_TEXT)
            .forEach { k -> x.getCharSequence(k)?.let { texts += it.toString() } }
        x.getCharSequenceArray(Notification.EXTRA_TEXT_LINES)?.forEach { texts += it.toString() }
        for (views in listOfNotNull(n.bigContentView, n.contentView, n.headsUpContentView)) {
            runCatching {
                val view = views.apply(this, android.widget.FrameLayout(this))
                fun walk(v: android.view.View) {
                    if (v is android.widget.TextView) v.text?.let { texts += it.toString() }
                    if (v is android.view.ViewGroup) for (i in 0 until v.childCount) walk(v.getChildAt(i))
                }
                walk(view)
            }
        }
        val levels = mutableMapOf<String, Int>()
        val pattern = Regex("""\b([LRC])\s*:\s*(\d{1,3})\s*%""")
        for (t in texts) for (m in pattern.findAll(t)) levels.putIfAbsent(m.groupValues[1], m.groupValues[2].toInt().coerceIn(0, 100))
        if (levels.isEmpty()) return false
        NotchBridge.toNotch(
            mapOf(
                "type" to "budsBattery",
                "name" to x.getCharSequence(Notification.EXTRA_TITLE)?.toString(),
                "left" to levels["L"],
                "right" to levels["R"],
                "case" to levels["C"],
            )
        )
        return true
    }

    private fun label(pkg: String) =
        runCatching { packageManager.getApplicationLabel(packageManager.getApplicationInfo(pkg, 0)).toString() }.getOrDefault(pkg)

    /** The app's own icon, once per app, to show and style the notification by. */
    private fun sendIcon(pkg: String) {
        if (!iconsSent.add(pkg)) return
        val png = runCatching { png(packageManager.getApplicationIcon(pkg), 96) }.getOrNull() ?: return
        NotchBridge.toNotch(mapOf("type" to "icon", "package" to pkg, "png" to png))
    }

    // ───────────── media ─────────────

    private fun pick(list: List<MediaController>) {
        val playing = list.firstOrNull { it.playbackState?.state == PlaybackState.STATE_PLAYING }
        val next = playing ?: list.firstOrNull { it.packageName == controller?.packageName } ?: list.firstOrNull()
        if (next?.sessionToken != controller?.sessionToken) {
            controller?.unregisterCallback(onMedia)
            controller = next
            next?.registerCallback(onMedia, NotchBridge.handler)
            lastArtKey = null
        }
        sendMedia()
    }

    private fun sendMedia() {
        val c = controller
        val meta = c?.metadata
        val state = c?.playbackState
        if (c == null || meta == null) {
            NotchBridge.toNotch(mapOf("type" to "media", "active" to false))
            return
        }
        val title = meta.getString(MediaMetadata.METADATA_KEY_TITLE) ?: meta.getString(MediaMetadata.METADATA_KEY_DISPLAY_TITLE)
        val artist = meta.getString(MediaMetadata.METADATA_KEY_ARTIST) ?: meta.getString(MediaMetadata.METADATA_KEY_ALBUM_ARTIST)
            ?: meta.getString(MediaMetadata.METADATA_KEY_DISPLAY_SUBTITLE)
        val artKey = "${c.packageName}|$title|$artist"
        var art: ByteArray? = null
        if (artKey != lastArtKey) {
            val bmp = meta.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART) ?: meta.getBitmap(MediaMetadata.METADATA_KEY_ART)
                ?: meta.getBitmap(MediaMetadata.METADATA_KEY_DISPLAY_ICON)
            art = bmp?.let { runCatching { png(it, 256) }.getOrNull() } ?: artFromUri[artKey] ?: coverFromNotification(c.packageName, title)
            if (art == null) loadArt(artKey, meta)
            // Players often send the title first and the cover a moment later: only a song with its cover counts as sent.
            if (art != null) lastArtKey = artKey else lookAgainForArt(artKey)
        }
        NotchBridge.toNotch(
            mapOf(
                "type" to "media",
                "active" to true,
                "package" to c.packageName,
                "app" to label(c.packageName),
                "title" to title,
                "artist" to artist,
                "art" to art,
                "artKey" to artKey,
                "playing" to (state?.state == PlaybackState.STATE_PLAYING || state?.state == PlaybackState.STATE_BUFFERING),
                "position" to (state?.position ?: 0L),
                "updated" to (state?.lastPositionUpdateTime ?: 0L),
                "speed" to (state?.playbackSpeed?.toDouble() ?: 1.0),
                "duration" to meta.getLong(MediaMetadata.METADATA_KEY_DURATION),
                "canSeek" to (((state?.actions ?: 0L) and PlaybackState.ACTION_SEEK_TO) != 0L),
            )
        )
    }

    /**
     * Skipped with the headphones, the screen off: some players (Spotify) fetch the cover only later and say so on no
     * channel the notch hears. Looked for again a few times (1.5 s, 4 s, 10 s), as long as that song plays.
     */
    private fun lookAgainForArt(artKey: String) {
        if (artMissingKey != artKey) {
            artMissingKey = artKey
            artRetries = 0
        }
        if (artRetries >= 3) return
        val delay = longArrayOf(1500, 4000, 10000)[artRetries++]
        NotchBridge.handler.postDelayed({
            if (artMissingKey == artKey && lastArtKey != artKey) sendMedia()
        }, delay)
    }

    /**
     * Spotify and others name the cover by a link (an https address or a content:// one) instead of handing it over:
     * fetched once in the background, then the song is sent again with it.
     */
    private fun loadArt(artKey: String, meta: MediaMetadata) {
        val uri = listOf(MediaMetadata.METADATA_KEY_ALBUM_ART_URI, MediaMetadata.METADATA_KEY_ART_URI, MediaMetadata.METADATA_KEY_DISPLAY_ICON_URI)
            .firstNotNullOfOrNull { meta.getString(it)?.takeIf { s -> s.isNotBlank() } } ?: return
        if (!artLoading.add(artKey)) return
        Thread {
            val bytes = runCatching {
                val parsed = android.net.Uri.parse(uri)
                val input = when (parsed.scheme) {
                    "http", "https" -> (java.net.URL(uri).openConnection() as java.net.HttpURLConnection).apply {
                        connectTimeout = 8000
                        readTimeout = 8000
                    }.inputStream
                    else -> contentResolver.openInputStream(parsed)
                }
                input?.use { android.graphics.BitmapFactory.decodeStream(it) }?.let { png(it, 256) }
            }.getOrNull()
            NotchBridge.handler.post {
                artLoading.remove(artKey)
                if (bytes != null) {
                    artFromUri[artKey] = bytes
                    if (lastArtKey != artKey) sendMedia()
                }
            }
        }.start()
    }

    /** Some players (Chrome, for one) put the cover only on their media notification, as its large icon. */
    private fun coverFromNotification(pkg: String, title: String?): ByteArray? = runCatching {
        val players = activeNotifications.filter {
            it.packageName == pkg && it.notification.extras.containsKey(Notification.EXTRA_MEDIA_SESSION)
        }
        // The notification of the session that plays (a browser has one per tab), else any with a picture.
        val token = controller?.sessionToken
        val n = (players.firstOrNull {
            it.notification.extras.getParcelable(Notification.EXTRA_MEDIA_SESSION, android.media.session.MediaSession.Token::class.java) == token
        } ?: players.firstOrNull { it.notification.getLargeIcon() != null })?.notification ?: return null
        // Right after a skip the notification may still show the song before: its cover isn't this song's.
        val shows = n.extras.getCharSequence(Notification.EXTRA_TITLE)?.toString()
        if (title != null && shows != null && shows != title) return null
        val drawable = n.getLargeIcon()?.loadDrawable(this) ?: return null
        png(drawable, 256)
    }.getOrNull()

    fun mediaAction(action: String, position: Long?) {
        val t = controller?.transportControls ?: return
        when (action) {
            "play" -> t.play()
            "pause" -> t.pause()
            "toggle" -> if (controller?.playbackState?.state == PlaybackState.STATE_PLAYING) t.pause() else t.play()
            "next" -> t.skipToNext()
            "previous" -> t.skipToPrevious()
            "seek" -> if (position != null) t.seekTo(position)
        }
    }

    companion object {
        /** Apps whose notification picture means something (the next turn). */
        val PICTURE_APPS = setOf("com.google.android.apps.maps", "com.waze")

        /** Notification.FLAG_PROMOTED_ONGOING (Android 16). */
        const val FLAG_PROMOTED_ONGOING = 0x00040000

        fun png(drawable: Drawable, size: Int): ByteArray {
            val bmp = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(bmp)
            drawable.setBounds(0, 0, size, size)
            drawable.draw(canvas)
            return png(bmp, size)
        }

        fun png(bitmap: Bitmap, max: Int): ByteArray {
            val scale = minOf(1f, max.toFloat() / maxOf(bitmap.width, bitmap.height))
            val b = if (scale < 1f) Bitmap.createScaledBitmap(bitmap, (bitmap.width * scale).toInt().coerceAtLeast(1),
                (bitmap.height * scale).toInt().coerceAtLeast(1), true) else bitmap
            return ByteArrayOutputStream().use { b.compress(Bitmap.CompressFormat.PNG, 100, it); it.toByteArray() }
        }
    }
}
