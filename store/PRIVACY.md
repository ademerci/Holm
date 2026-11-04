# Privacy policy for Holm for Windows

**Effective date: November 4, 2026**

Holm for Windows is published by Aleandro D'Urso, an individual developer, under the name Holm Team. This policy explains how Holm handles information on your device and when you use its connected features. You can contact the team at [dursoaleandro@gmail.com](mailto:dursoaleandro@gmail.com).

## At a glance

Holm keeps its settings and most feature data on your device. Its current code does not automatically send usage analytics, diagnostic logs, notification content or clipboard history to the Holm Team. Connected features can send information to other people or services, as described below. If you contact us or submit an issue, we receive what you choose to send.

## Information on your device

Holm stores settings and feature data locally. Depending on the features you use, this can include notes, reminders, shelf items, the selected weather location, friends, a protected friend key and queued file paths. Windows may store data for the packaged app separately from the portable version. Uninstalling the Store app may remove its app data; files saved to your Downloads folder remain there.

If enabled, clipboard history keeps recent text in memory and temporarily saves clipboard images as PNG files in Holm's temporary folder. Holm attempts to remove these files when they are no longer needed, but they may remain if cleanup fails or the app stops unexpectedly. Received text may also be placed on the clipboard. Clipboard history is processed locally; if you choose to Beam copied text, Holm temporarily saves it as a file and sends it to the recipient or service you select. Holm attempts to exclude content marked private by other apps, but not all sensitive content is marked. Screenshot text recognition uses Windows OCR on your device. Holm can watch your Downloads and Screenshots folders to show activity. You can disable these features in Holm's settings.

With your permission, Holm reads and processes Windows notifications locally to show message previews and counts; it does not automatically send notification content to the Holm Team. You can revoke notification access in Windows. Holm also reads information about media, devices, batteries, network connections and camera and microphone activity for its controls and indicators. The camera and microphone indicators detect activity; they do not read audio or video content. Relevant controls are available in Holm and Windows settings.

Holm can write error logs locally; optional diagnostic logging can write additional information locally when enabled. Logs may contain technical details and potentially sensitive information, so review them before sharing them with anyone. Holm's current code does not include a feature that automatically sends these logs or usage analytics to the Holm Team. If you contact us or submit an issue, we receive the information you choose to send.

## Weather

If automatic weather location is enabled and Windows provides a location, Holm rounds the coordinates to one decimal place and saves them. Holm sends the selected or rounded location to Open-Meteo for forecasts; a city search sends the entered place name. Open-Meteo can also receive your IP address and request metadata. You can disable automatic location and choose a place manually. See [Open-Meteo's terms and privacy notice](https://open-meteo.com/en/terms).

## Beam and friends

When Beam is enabled, Holm discovers nearby devices on the local network and can exchange files or text when you choose to send or accept them. Nearby transfers are encrypted, but devices on the local network are not authenticated; use a code on networks you do not trust. Phone sharing serves selected files to a browser on the local network through a temporary link. This connection uses HTTP and is not encrypted by HTTPS.

Code-based transfers use the public [magic-wormhole transit relay](https://magic-wormhole.readthedocs.io/en/latest/transit.html) to pass encrypted data. The relay can receive network addresses, connection timing and other connection metadata. Friend features use [ntfy.sh](https://docs.ntfy.sh/privacy/) by default, or a server you configure, for encrypted messages such as invitations and transfer notices. Friend messages are encrypted using keys held by the participants, so the ntfy server does not receive their plaintext. The server can see connection metadata, inbox identifiers and timing. ntfy.sh says it caches messages for 12 hours by default; retention and other data practices are described in its privacy policy. The operator of a custom server has its own practices. Your friend key is stored protected for your Windows account. People you send files or text to receive that content. See the [Holm README](https://github.com/ademerci/Holm#beam) for details and limitations. Turn Beam off in Holm's settings to stop its discovery and friend inbox.

## Other services

If you use Focumon integration, its browser extension sends Focumon status to Holm on your PC; Holm may fetch artwork from the relevant service. Opening an AI assistant or another linked app takes you to that service, whose own privacy terms apply. The portable GitHub version can check GitHub for updates; the Microsoft Store version receives updates through Microsoft Store infrastructure. These services receive the network information needed to respond to requests. See the [GitHub privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement) and [Microsoft privacy statement](https://privacy.microsoft.com/privacystatement).

## Retention, choices and requests

Local feature data remains on your PC until you change or remove it, clear the relevant app data, or uninstall and remove its data, subject to Windows' storage behavior. Temporary transfer files may be created during sharing. Received files in your personal folders may remain after uninstalling. Recipients and third-party services control information they receive under their own practices and retention rules.

You can disable individual features in Holm, revoke permissions in Windows, delete received files and uninstall Holm. Depending on where you live and the circumstances, you may have rights concerning your personal information, including rights to access, correct or delete it, or to complain to a data protection authority. Contact us using the address below for requests about information the Holm Team holds; for information handled by third-party services, contact the relevant provider. Deleting local data does not delete copies already received by another person or service.

## Children

Holm is a general-audience desktop app and can be used by people of different ages. The app does not ask users for their age or automatically send usage analytics or local logs to the Holm Team. Its local and connected features handle information as described in this policy; parents and guardians can manage relevant device permissions and settings.

## Changes and contact

We may update this policy as Holm or our practices change. We will publish the updated policy in the [Holm repository](https://github.com/ademerci/Holm) and update the effective date. For questions or requests, contact the Holm Team at [dursoaleandro@gmail.com](mailto:dursoaleandro@gmail.com) or through [GitHub Issues](https://github.com/ademerci/Holm/issues).
