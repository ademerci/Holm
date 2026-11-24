package ch.holmnotch.holm_mobile

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.net.Uri
import android.provider.Settings
import java.io.File

/**
 * Holm's own updates (lib/update.dart finds and downloads them): the "new version" notification and the install,
 * with Android's PackageInstaller. Android asks the person to confirm; then it replaces Holm in place.
 */
object Updates {
    const val EXTRA_UPDATE = "holm.update"
    private const val CHANNEL = "updates"
    private const val NOTIFICATION = 4242
    private const val PREFS = "holm_updates"

    fun currentVersion(context: Context): Long {
        val code = context.packageManager.getPackageInfo(context.packageName, 0).longVersionCode
        // Updated meanwhile: the notification about this version is old news.
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getLong("notified", 0) in 1..code) {
            context.getSystemService(NotificationManager::class.java).cancel(NOTIFICATION)
        }
        return code
    }

    fun notify(context: Context, code: Long, name: String) {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getLong("notified", 0) == code) return
        val nm = context.getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CHANNEL, "Updates", NotificationManager.IMPORTANCE_DEFAULT).apply {
            description = "A new version of Holm is ready"
        })
        fun open(request: Int, update: Boolean) = PendingIntent.getActivity(
            context, request,
            Intent(context, MainActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
                .putExtra(EXTRA_UPDATE, update),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val n = Notification.Builder(context, CHANNEL)
            .setSmallIcon(android.R.drawable.stat_sys_download_done)
            .setContentTitle("Holm $name is ready")
            .setContentText("Tap Update to install it.")
            .setContentIntent(open(1, false))
            .addAction(Notification.Action.Builder(null, "Update", open(2, true)).build())
            .setAutoCancel(true)
            .build()
        runCatching { nm.notify(NOTIFICATION, n) }
        prefs.edit().putLong("notified", code).apply()
    }

    /** "started" once Android has the APK, "permission" if Holm may not install apps yet (opens that setting). */
    fun install(context: Context, path: String): String {
        val pm = context.packageManager
        if (!pm.canRequestPackageInstalls()) {
            context.startActivity(
                Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            )
            return "permission"
        }
        val file = File(path)
        val installer = pm.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(context.packageName)
            setSize(file.length())
            setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
        }
        val id = installer.createSession(params)
        installer.openSession(id).use { session ->
            file.inputStream().use { input ->
                session.openWrite("holm.apk", 0, file.length()).use { out ->
                    input.copyTo(out)
                    session.fsync(out)
                }
            }
            val status = PendingIntent.getBroadcast(
                context, id, Intent(context, UpdateReceiver::class.java),
                PendingIntent.FLAG_MUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            session.commit(status.intentSender)
        }
        return "started"
    }
}

/** What the installer says: asks the person to confirm, or tells the app how it ended. */
class UpdateReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                val confirm = intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent::class.java) ?: return
                context.startActivity(confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            }
            PackageInstaller.STATUS_SUCCESS -> NotchBridge.toMain("updateStatus", mapOf("ok" to true, "message" to ""))
            else -> NotchBridge.toMain(
                "updateStatus",
                mapOf(
                    "ok" to false,
                    "message" to if (status == PackageInstaller.STATUS_FAILURE_ABORTED) "Update cancelled"
                    else intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE) ?: "The update didn't install",
                ),
            )
        }
    }
}
