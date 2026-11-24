package ch.holmnotch.holm_mobile

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import rikka.shizuku.Shizuku

/**
 * Shizuku (shizuku.rikka.app) runs commands with adb's rights on the phone itself. Holm uses it once: to give itself
 * WRITE_SECURE_SETTINGS, the permission that hides Android's own notification pop-ups ([NotchBridge.applyPopups]).
 * The grant stays after that; Shizuku isn't needed again.
 */
object ShizukuGrant {
    private const val PACKAGE = "moe.shizuku.privileged.api"
    private const val REQUEST = 4711

    /** "missing" (not installed), "stopped" (installed, not running), "ask" (running, Holm not allowed yet), "ready". */
    fun status(context: Context): String = when {
        !installed(context) -> "missing"
        !runCatching { Shizuku.pingBinder() }.getOrDefault(false) -> "stopped"
        runCatching { Shizuku.isPreV11() }.getOrDefault(true) -> "stopped"
        Shizuku.checkSelfPermission() != PackageManager.PERMISSION_GRANTED -> "ask"
        else -> "ready"
    }

    /** Asks Shizuku (if needed) and grants the permission; answers "granted", "denied", "missing", "stopped" or "failed". */
    fun unlock(context: Context, done: (String) -> Unit) {
        when (val s = status(context)) {
            "missing", "stopped" -> done(s)
            "ready" -> grant(context, done)
            else -> {
                lateinit var listener: Shizuku.OnRequestPermissionResultListener
                listener = Shizuku.OnRequestPermissionResultListener { code, result ->
                    if (code != REQUEST) return@OnRequestPermissionResultListener
                    Shizuku.removeRequestPermissionResultListener(listener)
                    if (result == PackageManager.PERMISSION_GRANTED) grant(context, done) else done("denied")
                }
                Shizuku.addRequestPermissionResultListener(listener)
                runCatching { Shizuku.requestPermission(REQUEST) }.onFailure {
                    Shizuku.removeRequestPermissionResultListener(listener)
                    done("failed")
                }
            }
        }
    }

    private fun grant(context: Context, done: (String) -> Unit) {
        Thread {
            val ok = runCatching {
                // Shizuku.newProcess is hidden in API 13 (it points to its user services); a one-off command is all this needs.
                val newProcess = Shizuku::class.java.getDeclaredMethod(
                    "newProcess", Array<String>::class.java, Array<String>::class.java, String::class.java
                ).apply { isAccessible = true }
                val cmd = arrayOf("pm", "grant", context.packageName, Manifest.permission.WRITE_SECURE_SETTINGS)
                val process = newProcess.invoke(null, cmd, null, null) as Process
                process.waitFor()
                process.exitValue() == 0
            }.getOrDefault(false)
            val granted = ok || NotchBridge.canHidePopups(context)
            done(if (granted) "granted" else "failed")
        }.start()
    }

    private fun installed(context: Context) =
        runCatching { context.packageManager.getPackageInfo(PACKAGE, 0); true }.getOrDefault(false)
}
