# Privacy policy for Holm for Windows

**Effective date: November 11, 2026**

Holm for Windows is published by Aleandro D'Urso, an individual developer, under the name Holm Team. This policy explains how Holm handles information on your device and when you use its connected features. You can contact the team at [dursoaleandro@gmail.com](mailto:dursoaleandro@gmail.com).

## At a glance

Holm keeps its settings and most feature data on your device. Its current code does not automatically send usage analytics, diagnostic logs, notification content or clipboard history to the Holm Team. Connected features can send information to other people or services, as described below. If you contact us or submit an issue, we receive what you choose to send.

## Information on your device

Holm stores settings and feature data locally. Depending on the features you use, this can include notes, reminders, shelf items, the selected weather location, friends, a protected friend key, profile information and pictures, local chat history (up to 300 messages per friend), achievement progress and queued file paths. Windows may store data for the packaged app separately from the portable version. Uninstalling the Store app may remove its app data; files saved to your Downloads folder remain there.

If enabled, clipboard history keeps recent text in memory and temporarily saves clipboard images as PNG files in Holm's temporary folder. Holm attempts to remove these files when they are no longer needed, but they may remain if cleanup fails or the app stops unexpectedly. Received text may also be placed on the clipboard. Clipboard history is processed locally; if you choose to Beam copied text, Holm temporarily saves it as a file and sends it to the recipient or service you select. Holm attempts to delete the temporary text file after sending, cancellation, or the phone link closes; it may remain if cleanup fails or the app stops unexpectedly. Holm attempts to exclude content marked private by other apps, but not all sensitive content is marked. Screenshot text recognition uses Windows OCR on your device. Holm can watch your Downloads and Screenshots folders to show activity. You can disable these features in Holm's settings.

With your permission, Holm reads and processes Windows notifications locally to show message previews and counts; it does not automatically send notification content to the Holm Team. You can revoke notification access in Windows. Holm also reads information about media, devices, batteries, network connections and camera and microphone activity for its controls and indicators. The camera and microphone indicators detect activity; they do not read audio or video content. Relevant controls are available in Holm and Windows settings.

Holm can write error logs locally; optional diagnostic logging can write additional information locally when enabled. Logs may contain technical details and potentially sensitive information, so review them before sharing them with anyone.

## Weather

If automatic weather location is enabled and Windows provides a location, Holm rounds the coordinates to one decimal place and saves them. Holm sends the selected or rounded location to Open-Meteo for forecasts; a city search sends the entered place name. Open-Meteo can also receive your IP address and request metadata. You can disable automatic location and choose a place manually. See [Open-Meteo's terms and privacy notice](https://open-meteo.com/en/terms).

## Beam and friends

**Nearby devices.** When Beam is on, Holm announces its display name, a session identifier and connection port to the local network in unencrypted discovery messages; nearby devices can also see its network address and availability. When you send to a nearby Holm, the apps establish a fresh key and encrypt the file offer and transfer using AES-256-GCM. The key exchange does not verify device identity, so someone on the network could impersonate a device or interfere with the connection. Use a code on networks you do not trust.

**Codes and friends.** For a code transfer, Holm generates a random secret, derives the encryption key from it and connects through the public [magic-wormhole transit relay](https://magic-wormhole.readthedocs.io/en/latest/transit.html). The sender name, offered file names, sizes and file contents travel inside the encrypted connection. The relay can see network addresses, connection timing, traffic sizes and a transfer identifier, but does not receive the code or encryption key. Anyone with the code can attempt to join the transfer, so share it only with the intended recipient over a trusted channel. The recipient is asked to accept the offer before file content is sent.

For friends, Holm sends a fresh transfer secret in an encrypted friend message rather than requiring you to pass a code for each transfer. Friend codes contain the person's display name, inbox identifier, public key and, if configured, server address; verify whose code you add. Friend messages use keys held by the participants and are encrypted with AES-256-GCM. The [ntfy.sh](https://docs.ntfy.sh/privacy/) server used by default, or a custom server you configure, does not receive the message plaintext or transfer files, but can see the sender's public key, inbox identifier, network addresses, timing and message sizes. ntfy.sh says it caches messages for 12 hours by default; custom servers may have different retention and privacy practices. The friend private key is stored protected for your Windows account.

**Phone.** Holm serves selected files and text directly from your PC to a browser over the local network using a temporary link with a random token. This connection uses HTTP, not HTTPS: the contents and link can be visible to someone able to observe the network, and anyone who obtains the link while it is active can access the offered files. The link closes when you choose Done, after ten minutes, or shortly after all files are delivered. Use a trusted network and do not share the link with others.

**Storage and controls.** Before sending, folders may be packed as temporary ZIP files and copied text may be saved as a temporary text file. If Send later is used, Holm keeps queued paths and may retain its temporary files across restarts until the send is completed, declined, cancelled or expires. Holm attempts to clean up temporary and incomplete files, but they can remain if cleanup fails or the app stops unexpectedly; completed files and copies received by others can remain. Turn Beam off in Holm's settings to stop nearby discovery and the friend inbox. See the [Holm README](https://github.com/ademerci/Holm#beam) for further details.
## Profiles, chat, activity and screen streaming

Your profile picture, name, status and color are sent to your friends in encrypted friend messages. Chat messages use the same encrypted friend messaging service described above; message history is stored on each participant's PC. Recipients can retain or share what they receive.

When Share my activity is enabled (on by default), friends receive your current activity, which may include the playing song, a call, fullscreen game, timer or stream, and your achievement count. Activity travels in encrypted presence messages through the configured friend inbox service. Turn Share my activity off under Friends settings to stop sharing it. Achievement progress is stored locally.

Screen streaming starts when you choose to invite a friend, and the viewer must accept the invitation. Holm captures screen images and sends JPEG frames through an encrypted SecureChannel over the transit relay described above. The relay can observe addresses, timing and traffic sizes but does not receive the stream secret or unencrypted frames. The Holm window, notch and LIVE badge are excluded from capture; other visible screen content may contain personal information. Review your screen before inviting viewers. Use Stop to end sharing, or close the viewer window to stop watching. Viewers can capture or retain what is displayed.

## GIFs, reactions and invite codes

Chat reactions are shared in encrypted friend notes. GIF search sends search terms and request metadata to GIPHY using the configured API key. GIF links are included in encrypted chat notes, and displaying a GIF downloads it over HTTPS from its host. GIPHY and GIF hosts can receive your IP address and request information. GIF files are cached locally. See [GIPHY privacy information](https://giphy.com/privacy).

Short friend invite codes provide access to an encrypted friend card stored on the configured inbox server. The card includes the information needed to add you as a friend. Codes last 12 hours and anyone with a valid code can add you automatically. Share them only with intended friends. Pending friend requests are stored locally for up to two days.

The audio-reactive equalizer reads the default audio output locally to animate its bars. These audio samples are not sent to the Holm Team or other services. You can disable this under General settings.

## Other services

If you use Focumon integration, its browser extension sends Focumon status to Holm on your PC; Holm may fetch artwork from the relevant service. Opening an AI assistant or another linked app takes you to that service, whose own privacy terms apply. The portable GitHub version can check GitHub for updates; the Microsoft Store version receives updates through Microsoft Store infrastructure. These services receive the network information needed to respond to requests. See the [GitHub privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement) and [Microsoft privacy statement](https://privacy.microsoft.com/privacystatement).

## Retention, choices and requests

Local feature data remains on your PC until you change or remove it, clear the relevant app data, or uninstall and remove its data, subject to Windows' storage behavior. Temporary transfer files may be created during sharing. Received files in your personal folders may remain after uninstalling. Recipients and third-party services control information they receive under their own practices and retention rules.

The Holm Team generally holds no personal information about you beyond what you choose to send us, such as in support emails or GitHub issues. You can disable individual features in Holm, revoke permissions in Windows, delete received files and uninstall Holm. Depending on where you live and the circumstances, you may have rights concerning your personal information, including rights to access, correct or delete it, or to complain to a data protection authority. Contact us using the address below for requests about information the Holm Team holds; for information handled by third-party services, contact the relevant provider. Deleting local data does not delete copies already received by another person or service.

## Children

Holm is a general-audience desktop app and can be used by people of different ages. The app does not ask users for their age. Its local and connected features handle information as described in this policy; parents and guardians can manage relevant device permissions and settings.

## Changes and contact

We may update this policy as Holm or our practices change. We will publish the updated policy in the [Holm repository](https://github.com/ademerci/Holm) and update the effective date. For questions or requests, contact the Holm Team at [dursoaleandro@gmail.com](mailto:dursoaleandro@gmail.com) or through [GitHub Issues](https://github.com/ademerci/Holm/issues).
