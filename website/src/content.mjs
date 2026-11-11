// Product facts: ../README.md, ../CHANGELOG.md and the app's Settings pages.
export const docs = [
  {
    slug: 'getting-started',
    title: 'Getting started',
    group: 'Start here',
    intro: 'Your desktop, with a little more within reach.',
    sections: [
      [
        'Meet the notch',
        'Holm lives at the top of your screen. Open it for your music, today’s weather and everyday tools. Right-click it for Settings, timers, friends and more. A click outside closes the open notch.',
      ],
      [
        'Your first few minutes',
        'Download the Windows x64 release, place Holm.exe in a folder you can write to and run it. Start some music in Spotify, a supported browser or another Windows media player. Open the notch to see its controls.',
      ],
      [
        'Make it yours',
        'Right-click the notch and choose Settings. Choose your collapsed and expanded sizes independently, adjust the open notch, and turn individual pop-outs on or off. Most settings wait for Apply.',
      ],
      [
        'Keep exploring',
        'Try a note, set a reminder in plain words, or drag a file onto the notch to see Shelf and Beam. You can use Shelf with Beam switched off.',
      ],
    ],
  },
  {
    slug: 'installation',
    title: 'Installation & requirements',
    group: 'Start here',
    intro: 'One Windows app. No separate .NET installation.',
    sections: [
      [
        'System requirements',
        'Windows 10 version 2004 or newer, or Windows 11, on an x64 PC. The published GitHub executable is self-contained. macOS, Linux and native ARM64 builds are not offered here.',
      ],
      [
        'Install from GitHub',
        'On the Download page, choose Holm.exe to run the app directly, or download the Windows x64 ZIP, extract it and run Holm.exe. Keep the app in a folder your Windows account can write to so the portable updater can replace it.',
      ],
      [
        'Windows security prompts',
        'The current GitHub release is not code-signed. Windows SmartScreen may warn at first launch, and Smart App Control may block an unsigned app. Check the official release and its source before deciding whether to run it. Holm does not require you to disable Windows security protections.',
      ],
      [
        'Microsoft Store',
        'Holm for Windows is available from Microsoft Store, published by Holm Team. The verified Store release is 1.9.0 (package 1.9.0.0). This channel receives less frequent releases with additional validation; the Store handles its updates. GitHub receives newer public builds first. The guide describes the current GitHub app, so some recent features may not be in the Store version yet.',
      ],
    ],
  },
  {
    slug: 'configuration',
    title: 'Make Holm yours',
    group: 'Everyday use',
    intro: 'A small space that fits the way you work.',
    sections: [
      [
        'Open Settings',
        'Right-click the notch → Settings. The Holm window has a sidebar for your profile, Active now, Chat, Achievements and settings pages. Press Esc to close it. Most changes take effect after Apply; the automatic update check switch takes effect immediately.',
      ],
      [
        'Size and screen',
        'Choose Small, Normal, Large or Extra large independently for the collapsed notch and expanded panel. Pop-outs and Beam prompts follow the collapsed size. Use the right-click menu to pick a screen.',
      ],
      [
        'Weather',
        'If no place is saved, Holm tries Windows approximate location once at startup. Coordinates are rounded to one decimal place before saving or sending them to Open-Meteo. Windows location services must be on; you can choose a city manually instead. Forecasts refresh at most every 30 minutes.',
      ],
      [
        'Space for browser tabs',
        'Settings → General → Hide over browser tabs lets the resting notch step aside over a maximized browser. Pop-outs and calls bring it back. This setting is off by default. Fullscreen games and videos hide the notch automatically.',
      ],
      [
        'Quiet hours',
        'Set a start and end time to silence message pop-outs while retaining unread badges. Apps and people you allow can still interrupt.',
      ],
    ],
  },
  {
    slug: 'media',
    title: 'Music & sound',
    group: 'Everyday use',
    intro: 'The track, the volume and the next song, together.',
    sections: [
      [
        'Playback',
        'Spotify, YouTube in Chrome or Edge, VLC and other Windows media sessions can appear in Holm. The open player has artwork, track information, seeking, playback controls, shuffle, repeat and volume. Available controls depend on what the player exposes to Windows. Click the artwork to bring that player forward.',
      ],
      [
        'Album colours and equalizer',
        'The notch uses a blurred copy of the album artwork and crossfades when the track changes. While its bars are visible, the equalizer reads output loudness, not your microphone. Nothing is recorded or kept. Settings → General → Equalizer follows the music switches to the fixed animation.',
      ],
      [
        'Output and per-app volume',
        'When multiple output devices are available, use the button beside volume to choose one for music, games and calls. The mixer gives each app playing audio its own slider and mute control.',
      ],
      [
        'Sleep timer and calls',
        'The moon in the open player stops music after 15, 30 or 60 minutes. Holm can pause music during calls and resume it afterwards. Turning off Media controls under Settings → Open notch also disables call-driven pausing and the sleep timer.',
      ],
    ],
  },
  {
    slug: 'tools',
    title: 'Notes, reminders & tools',
    group: 'Everyday use',
    intro: 'For the little things between the big things.',
    sections: [
      [
        'Notes and reminders',
        'The note stays on this PC and survives a restart. Type reminders such as “20 min laundry”, “14:30 dentist” or “tomorrow 9:00 call”; German works too. Reminders survive a restart, and one missed while your PC was off appears when it comes back. A due reminder offers +10 min.',
      ],
      [
        'Calculator and colour picker',
        'Type a calculation and click the result to copy it. Percentage calculations such as “50 + 15%” work too. With the colour picker, click a screen pixel to copy its hex code; arrow keys move one pixel, Shift moves ten, and Enter picks.',
      ],
      [
        'Clipboard history',
        'Holm keeps the last 20 copied texts and pictures in memory. Click one to copy it again. Passwords marked private by password managers are excluded. A copied password is cleared after 30 seconds if it is still on the clipboard. History is not saved across restarts.',
      ],
      [
        'Screenshots and text recognition',
        'Windows screenshots and snips can appear in a small pop-out with Send, Shelf and Copy text. Text recognition uses Windows OCR on your PC. Images copied from unrelated apps are left alone.',
      ],
      [
        'Shelf and timers',
        'Drop files or folders on Shelf to keep their paths within reach. Drag them back into another window when ready; Explorer-style move and copy behaviour applies. Right-click → Timer offers durations from one minute to one hour, with pause, stop and five more minutes when time is up.',
      ],
    ],
  },
  {
    slug: 'beam',
    title: 'Beam & friends',
    group: 'Stay connected',
    intro: 'From your notch to theirs.',
    sections: [
      [
        'Send a file',
        'Drag files or folders onto the notch. Pick a friend, someone nearby, Phone or Code. The recipient must accept a Holm transfer. Received files land in Downloads; folders travel as ZIP files.',
      ],
      [
        'Add a friend',
        'In Holm 1.10 or newer, open Add a friend from the Holm window, the + beside chat search, or Friends → Add a friend in the right-click menu. Share the short invite code; someone entering it on their Add a friend page is added without another acceptance step. A code lasts 12 hours and New code replaces it. Invite codes require Holm 1.10 on both PCs. For the older Store release, use Copy my long friend code and the legacy request flow; the recipient accepts or declines the request. The Add a friend page also lists pending requests.',
      ],
      [
        'Nearby and codes',
        'Nearby discovers Holm on the same network. Code works across different networks: share the copied code privately, then the recipient copies it or chooses Receive with copied code. Codes work once and expire after 15 minutes. Use Code on networks you do not trust; nearby devices do not authenticate each other’s identities.',
      ],
      [
        'Send to a phone',
        'Choose Phone to show a QR code. Scan it with your phone’s camera and download in its browser. The phone must share the PC’s Wi-Fi network. This is a temporary local HTTP link, not the encrypted Holm-to-Holm transfer channel. It closes on Done, after ten minutes, or shortly after all files arrive.',
      ],
      [
        'When a friend is away',
        'With Send later on, files wait on your PC until the friend’s Holm answers. Holm retries for up to a week. The sending PC and files need to remain available; this is not cloud file storage.',
      ],
    ],
  },
  {
    slug: 'chat',
    title: 'Chat & screen sharing',
    group: 'Stay connected',
    intro: 'Keep a conversation close.',
    sections: [
      [
        'Conversations',
        'Right-click → Friends and chat opens conversations in the Holm window. Send text, quick reactions and GIFs, or click + beside the message box to choose files. File transfers use Beam and require it to be enabled.',
      ],
      [
        'Messages while offline',
        'A friend whose Holm is closed gets a sealed chat note when it starts, while that note remains in the inbox (about 12 hours). Chat history stays on your PC, with the last 300 messages per friend.',
      ],
      [
        'Screen sharing',
        'Choose Share my screen in a friend’s chat. The friend receives an invitation to watch. Sharing stops when you choose Stop, close the stream window or the connection is silent for 20 seconds. Holm’s own notch and window are excluded from the capture.',
      ],
      [
        'Presence',
        'Active now can show a friend’s stream, call, fullscreen game, music or timer. Share my activity turns off sharing your own activity.',
      ],
    ],
  },
  {
    slug: 'privacy',
    title: 'Privacy & notifications',
    group: 'Reference',
    intro: 'Know what the notch can see.',
    sections: [
      [
        'Messages from other apps',
        'Holm reads supported apps’ Windows notifications and numeric taskbar badges. It requests notification access at first start. Supported messengers include WhatsApp, Discord, Outlook, Telegram, Signal and Slack, including supported browser versions. Teams messages are intentionally excluded. Individual apps can be switched off.',
      ],
      [
        'Notification limitations',
        'Notification access varies with Windows and application behaviour. Some apps leave no notification, so Holm may show an app name and unread count instead of message text. Holm does not remove or change Windows notifications and does not write message text to logs.',
      ],
      [
        'Microphone and camera indicators',
        'Orange and green dots indicate recording reported by Windows. Holm does not read audio or video content for these indicators. Some legacy DirectShow virtual cameras are not reported reliably. Settings → Calls and privacy can turn indicators off.',
      ],
      [
        'Local data and external services',
        'Notes, chat history and settings stay on your PC. Weather uses Open-Meteo; GIF search uses GIPHY. Beam can use ntfy.sh for sealed signalling and a magic-wormhole transit relay for encrypted transfers. Service operators can still observe connection metadata. See the repository README for the detailed privacy and network model.',
      ],
    ],
  },
  {
    slug: 'updates',
    title: 'Updates',
    group: 'Reference',
    intro: 'Choose your pace. Keep your settings.',
    sections: [
      [
        'Portable GitHub updates',
        'Settings → Updates → Check for updates looks for a stable GitHub release. Automatic checks are on by default, shortly after startup and every two hours. Downloads and installation begin only when you choose Update and restart.',
      ],
      [
        'Installing an update',
        'Use a folder your account can write to. Holm waits for file-sharing activities to finish before offering to restart, keeps a backup during replacement and restores it if the new app cannot start. Settings, friends and shortcuts stay in place. Development builds can check but cannot replace themselves.',
      ],
      [
        'Release channels',
        'GitHub receives newly published public builds first. A GitHub release is only a prerelease when its publisher marks it as one. Microsoft Store currently offers Holm 1.9.0, updates less often with additional validation and can lag behind GitHub. Store-installed Holm uses Store-managed updates rather than the portable GitHub updater.',
      ],
    ],
  },
  {
    slug: 'troubleshooting',
    title: 'Troubleshooting & FAQ',
    group: 'Reference',
    intro: 'A few things that help.',
    sections: [
      [
        'No music in the notch?',
        'Check Settings → Open notch → Media controls. Start playback in a player that exposes a Windows media session. Some websites or players expose fewer controls than others.',
      ],
      [
        'No weather?',
        'Windows location services are needed for automatic setup. Choose a city manually in Weather if location is unavailable. Forecasts refresh at most every 30 minutes.',
      ],
      [
        'Missing messages?',
        'Allow notification access in Windows Settings → Privacy & security → Notifications, and check the app’s own notification settings. Numeric unread badges can still work when message previews are unavailable. Teams messages are not supported.',
      ],
      [
        'Phone cannot open the QR link?',
        'Both devices need to be on the same network and the share must still be open. Guest or school networks may isolate devices. Phone sharing is local; Holm-to-Holm Code transfers are the option for separate networks.',
      ],
      [
        'Does Holm support Mac or ARM64?',
        'The published build targets Windows x64. No native macOS, Linux or ARM64 build is advertised.',
      ],
      [
        'Report a problem',
        'Open an issue in the official GitHub repository. Include the Holm version, Windows version and steps to reproduce. Avoid posting private messages, friend codes or transfer codes.',
      ],
    ],
  },
];

export const featureGroups = [
  {
    id: 'music',
    label: 'Music & sound',
    title: 'Your music. A little closer.',
    description:
      'A glance for the track. A click for the controls. Holm brings Windows media sessions to the top of your screen, with colours drawn from the album artwork.',
    image: 'expanded',
    alt: 'Actual Holm expanded media player with artwork, playback controls, tools and headphone battery',
    items: [
      [
        'The essentials, together',
        'Play, pause, skip and seek. Adjust volume, shuffle and repeat when supported by your player.',
      ],
      [
        'Sound, your way',
        'Switch output devices, connect paired Bluetooth audio devices and adjust volume for each app.',
      ],
      [
        'A quieter end to the day',
        'Set a sleep timer, or let Holm pause the music for your call and resume afterwards.',
      ],
    ],
    doc: 'media',
  },
  {
    id: 'everyday',
    label: 'Everyday tools',
    title: 'Less switching. More doing.',
    description:
      'A note before you forget it. A reminder in your own words. The small tools you reach for all day, in one familiar place.',
    image: 'today',
    alt: 'Actual Holm today panel with time, weather, reminder, note and tools',
    items: [
      [
        'A little room for today',
        'Time, weather, your next reminder and your note. Useful before you even start the music.',
      ],
      [
        'Tools within reach',
        'A calculator, clipboard history, colour picker, reminders and a per-app mixer.',
      ],
      [
        'Take it from here',
        'Park files on Shelf. Copy text from a screenshot using Windows OCR on your own PC.',
      ],
    ],
    doc: 'tools',
  },
  {
    id: 'beam',
    label: 'Beam & friends',
    title: 'Drop it. Send it. Done.',
    description:
      'Drag a file onto the notch and choose where it goes: a friend, someone nearby, a phone or a one-time code.',
    image: 'airdrop-share',
    alt: 'Actual Holm Beam file-sharing chooser with friends and a Code option',
    items: [
      [
        'Across the room. Or further.',
        'Discover Holm on your network, or use a friend or code to send across different networks.',
      ],
      [
        'Your phone, without another app',
        'Scan a QR code to get files in your phone’s browser over the same Wi-Fi.',
      ],
      [
        'A conversation, too',
        'Chat, react, send GIFs and invite a friend to watch your screen from the Holm window.',
      ],
    ],
    doc: 'beam',
  },
  {
    id: 'awareness',
    label: 'Quiet awareness',
    title: 'In the loop. Out of the way.',
    description: 'The right heads-up at the right moment. Then back to a small, quiet notch.',
    image: 'screenshot',
    alt: 'Actual Holm screenshot notification with Copy text, Shelf and Send',
    items: [
      [
        'Small things, noticed',
        'Headphone battery, volume changes, downloads, USB drives, VPN and connection status.',
      ],
      [
        'A glance for your messages',
        'Previews and unread counts for supported messengers. Choose which apps can interrupt, and when.',
      ],
      [
        'Space to focus',
        'Quiet hours, separate panel sizes, fullscreen hiding and an optional setting that makes room for browser tabs.',
      ],
    ],
    doc: 'configuration',
  },
];
