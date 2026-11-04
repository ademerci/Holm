# Holm for Windows privacy policy (draft)

This draft describes the current Holm desktop app. The Holm Team should review it, add an effective date and a reliable contact method, and publish it at a public URL before using it as the Microsoft Store privacy policy.

## Information Holm handles on your device

Holm stores its settings and feature data on your PC. Depending on the features you use, this can include your note, reminders, shelf items, chosen weather location, friends, encrypted friend key and queued file paths. Windows may keep packaged app data separately from the portable version. Uninstalling the Store app may remove its app data; files you received in Downloads remain there.

Holm can show recent clipboard text and pictures in memory and can place received text on the clipboard. It attempts to exclude content marked private by other apps; not all sensitive clipboard content is necessarily marked. Screenshot text recognition uses Windows OCR on your device. When enabled, Holm watches your Downloads and Screenshots folders to show activity. You can turn these features off in Holm's settings.

With your permission, Holm reads notifications to show message previews and counts. Windows lets you revoke notification access. Holm also reads Windows media, device, battery, network, camera and microphone activity information to show its indicators and controls. Camera and microphone indicators monitor activity, not the content of recordings. Windows privacy settings and Holm's settings provide controls for relevant features.

## Weather

If automatic weather location is enabled and Windows provides a location, Holm rounds the coordinates to one decimal place and saves them. Holm sends the selected or rounded location to Open-Meteo to obtain weather forecasts; a city search sends the entered place name to Open-Meteo. Open-Meteo receives your network address as part of those requests. You can disable automatic location and choose a place manually. See [Open-Meteo](https://open-meteo.com/) for its policies.

## Beam and friends

When Beam is enabled, Holm discovers nearby devices on the local network and can exchange files or text when you choose to send or accept them. Nearby transfers are encrypted, but devices on the local network are not authenticated; use a code on networks you do not trust. Phone sharing serves selected files over the local network to a browser with the temporary link. The phone-sharing connection is local and is not encrypted by HTTPS.

Code-based transfers use the public magic-wormhole transit relay to pass encrypted data. The relay can see connection metadata such as network addresses and timing. Friend features use ntfy.sh by default, or a server you configure, for encrypted messages such as invitations and transfer notices. The server can see connection metadata, inbox identifiers and timing. Your friend key is stored protected for your Windows account. People you send files or text to receive that content. See the [Holm README](https://github.com/ademerci/Holm#beam) for details and limitations. Turn Beam off in Holm's settings to stop its discovery and friend inbox.

## Optional services and updates

If you use Focumon integration, the browser extension sends Focumon status to Holm on your PC; Holm may fetch artwork from the relevant service. Opening an AI assistant or another linked app takes you to that service, whose own privacy terms apply. The portable GitHub version can check GitHub for updates; the Microsoft Store version receives updates through the Store. Those services receive the network information needed to respond to requests.

## Retention and choices

Feature data remains on your PC until you remove it, change it in Holm or uninstall and remove its data, subject to Windows' storage behavior. Temporary transfer files may be created during sharing. Recipients and third-party services control information they receive under their own policies. You can disable individual features in Holm, revoke permissions in Windows, delete received files, and uninstall Holm.

## Contact and changes

For questions or requests, contact the Holm Team through the [Holm GitHub repository](https://github.com/ademerci/Holm). The team should update this policy when app behavior changes and record the effective date when publishing it.
