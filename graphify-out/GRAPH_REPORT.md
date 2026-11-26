# Graph Report - Holm  (2026-11-14)

## Corpus Check
- 325 files · ~199,425 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 8 file(s) not represented in the graph (top: (none) 4, .css 2, .manifest 1)

## Summary
- 4277 nodes · 13032 edges · 192 communities (124 shown, 68 thin omitted)
- Extraction: 87% EXTRACTED · 13% INFERRED · 0% AMBIGUOUS · INFERRED: 1647 edges (avg confidence: 0.85)
- Token cost: 247,789 input · 0 output

## Community Hubs (Navigation)
- WPF Animation & Notch State
- MainWindow XAML Elements
- Network Details & Routes
- App Settings Toggles
- GIF Decoding & Animation
- Notification Badges & IO
- Bluetooth & Caps Lock Menu
- Friends Service
- Encrypted Stream Crypto
- Test & Sharing Namespaces
- Beam Text & Tool Clicks
- Audio Session COM Interfaces
- Power, Clipboard & Game Detection
- HTTP Client & Fake Handlers
- Updater Install Plan
- TCP Routing & Focumon Link
- Achievements & Chat Receive
- WinNotch Namespace Imports
- xUnit Theory Tests
- Native Structs & Color Pick
- Settings Menu Items
- Friend Code Cards & ECDH
- Sealed Friend Notes
- Focumon Handshake Verdict
- Chat Send & Delivery
- Media Transport Controls
- Audio Endpoint Devices
- Social Context Delegates
- Per-App Volume Sessions
- Website Client Script
- Clipboard History
- Hub Page & Settings Groups
- Changelog Features
- Beam Shelf & HTTP Streams
- Audio Meter Capture
- Device Change Listener
- Achievement Book
- USB Drive Detection
- Notch Panels & Pages
- Friend Invites
- Nearby LAN Discovery
- File Transfer
- Chat Page UI
- Tool Buttons
- Screenshots & Task Pictures
- Color Picker & Avatars
- Badge & Art Hosts
- Shell Icons & Messenger Logos
- Screen Capture Loop
- Bluetooth Devices
- Send Later Queue
- Windows Notification Listener
- Update Menu Tests
- Camera Activity Monitor
- Add Friend Page
- Privacy Monitor
- Repo & Release Conventions
- Release Workflow
- Notch View Kinds
- Updates & System Imports
- Focumon Extension Readers
- Timer & Sleep Menus
- Network List Manager COM
- Calculator Parser
- Quiet Hours
- Friend Note Kinds
- Activity Page
- Footprint Workflow
- Test Workflow
- Audio Session Events
- AI Launcher
- Website Build Script
- Folder Pack ZIP
- Bluetooth Battery
- Note DTO Fields
- Endpoint Volume COM
- Countdown Timer
- Website package.json
- Reminder Parser
- Media Info Updates
- Monitor Enumeration
- Profile Editor
- Release 1.7.0 Tools
- Network Page
- Messenger Detection
- Reminder List
- Chat Extras & Emoji
- Call & Browser Detection
- WinSqlite Wrapper
- App Volume List
- Friend Profiles
- Notch Sizing Tests
- Focumon Phases
- Screenshot Asset Tests
- Call Pause
- Notch State Enum
- Message Badge Tests
- Caps Lock Watcher
- Mini Slider
- Weather Kinds
- Download Watcher Tests
- Download Watcher
- Disk & Download Events
- Connection Outage
- Idle Sky & Motion
- App Startup & Mutex
- Battery Glyph
- Release Notes: Beam & Status
- Release Notes: Social & Settings
- Clipboard Password Wipe
- Friend Presence
- Badge Counter
- Screen OCR Text
- Weather Fetching
- Screenshot Prep Script
- Privacy Registry Reads
- Microphone Activity
- Test Project Dependencies
- QR Code & Weather Icons
- Release Notes: Today & Weather
- Release Notes: Updates & Build
- Release Notes: GIFs & Chat
- Disk Space
- Privacy Monitor Tests
- Weather Tests
- Notify Namespace
- Website Workflow
- Workflow Steps & Env
- Pages Deploy Job
- Shell Item COM
- Release Notes: Privacy Dots
- Wave Rate Limit
- Chat Reactions
- Screenshot Watcher
- Focumon Manifest
- Workflow Triggers
- Website Release Data
- Stream Viewer Window
- Exit Watchdog
- Notepad
- Settings Tests
- Weather Location
- IProcess group
- Holm 1 group
- CapsKey group
- ClipboardHistory group
- Tool group
- ShareCode group
- defaults group
- default group
- with group
- Browsers group
- InviteResult group
- MainWindow group
- Row group
- Autostart group
- content group
- IEasingFunction group
- Holm 1 group
- Holm 1 group
- Bar1 group
- CalcBox group
- AccessState group
- Clipboard history privacy and  group
-  group
- FriendRequestResult group
- DevicesPanel group
- ProfilePicture group
- Focumon integration in the not group
- MakeIcon group
- screenshots/manifest group
- CameraDot group
- Bluetooth device icon classifi group
- Design and accessibility appro group
- Cp group
- MonitorSeparator group
- PART_ContentHost group
- PlayIcon group
- Damped spring open/close anima
- Tests (dotnet test

## God Nodes (most connected - your core abstractions)
1. `MainWindow` - 672 edges
2. `Window` - 266 edges
3. `AppSettings` - 123 edges
4. `WinNotch` - 121 edges
5. `Friend` - 105 edges
6. `TextBlock` - 80 edges
7. `FriendsService` - 72 edges
8. `WinNotch.Tests` - 64 edges
9. `TempFolder` - 59 edges
10. `Native` - 57 edges

## Surprising Connections (you probably didn't know these)
- `Self-contained single-file Windows x64 release (Holm.exe)` --conceptually_related_to--> `Main download selection (newest stable with Holm.exe)`  [INFERRED]
  AGENTS.md → website/README.md
- `Store-managed updates vs portable updater` --semantically_similar_to--> `data/store.json`  [INFERRED] [semantically similar]
  AGENTS.md → website/README.md
- `Recorder` --references--> `DownloadWatcher`  [EXTRACTED]
  WinNotch.Tests/DownloadWatcherTests.cs → WinNotch/Notify/DownloadWatcher.cs
- `Inbox` --references--> `FriendNote`  [EXTRACTED]
  WinNotch.Tests/FriendsTests.cs → WinNotch/Sharing/Friends.cs
- `Running` --references--> `ScreenshotWatcher`  [EXTRACTED]
  WinNotch.Tests/ScreenshotWatcherTests.cs → WinNotch/ScreenshotWatcher.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Features carried by sealed friend notes over ntfy.sh** — changelog_friends_sealed_notes, changelog_chat, changelog_reactions, changelog_profiles, changelog_active_now, changelog_streaming, changelog_send_later, changelog_invite_codes [INFERRED 0.85]
- **Release distribution: portable Holm.exe, Store MSIX and website downloads** — agents_self_contained_single_file_release, agents_microsoft_store_msix_branch, agents_store_vs_portable_updates, website_readme_main_download_selection, website_readme_store_json, changelog_in_app_updater [INFERRED 0.85]
- **Website release data flow** — website_scripts_refresh_releases, website_readme_releases_json, website_readme_releases_mjs, website_src_client, github_workflows_website [INFERRED 0.85]
- **Privacy Indicator and Call Detection** — publish_release_1_6_1_release_notes_dual_privacy_dots, publish_release_1_7_3_release_notes_discord_call_fix, publish_release_1_7_4_release_notes_live_microphone_monitoring, publish_release_1_7_4_release_notes_live_camera_monitoring, publish_release_1_7_4_release_notes_privacy_dots_inside_notch, publish_release_1_7_5_release_notes_camera_indicator_regression_test [INFERRED 0.85]
- **Beam Transfer Features** — publish_release_1_5_5_release_notes_beam, publish_release_1_7_0_release_notes_beam_text, publish_release_1_7_0_release_notes_send_folders, publish_release_1_7_0_release_notes_send_later, publish_release_1_7_9_release_notes_open_beamed_file, publish_release_1_9_5_release_notes_files_in_chat [EXTRACTED 1.00]
- **Chat GIF Support Evolution** — publish_release_1_9_2_release_notes_gifs, publish_release_1_9_3_release_notes_giphy_out_of_box, publish_release_1_9_4_release_notes_gif_crash_proofing, publish_release_1_9_5_release_notes_giphy_key_fix, publish_release_1_11_0_release_notes_holm_window_memory_release [INFERRED 0.85]

## Communities (192 total, 68 thin omitted)

### Community 0 - "WPF Animation & Notch State"
Cohesion: 0.03
Nodes (14): MainWindow, ClosesOnClickAway, HasFocumon, AllViews, PhoneShowing, ScreenshotShowing, TaskShowing, ShelfItems (+6 more)

### Community 1 - "MainWindow XAML Elements"
Cohesion: 0.05
Nodes (82): AppText, ArtistText, ArtPlaceholder, BadgeText, BluetoothIcon, CalcHint, CalcPlaceholder, CalcResult (+74 more)

### Community 2 - "Network Details & Routes"
Cohesion: 0.05
Nodes (14): NetworkDetails, NetworkInfo, Candidate, LanAddress, NetworkLink, SpeedTest, NetworkInfoTests, SpeedTestTests (+6 more)

### Community 3 - "App Settings Toggles"
Cohesion: 0.03
Nodes (74): AppSettings, AchievementPopups, Ai, AiPath, Animations, AppVolume, Beam, BeamText (+66 more)

### Community 4 - "GIF Decoding & Animation"
Cohesion: 0.06
Nodes (18): GifAnimation, Bytes, Delays, Duration, Frames, Height, Width, GifCache (+10 more)

### Community 5 - "Notification Badges & IO"
Cohesion: 0.09
Nodes (12): NotificationBadges, DefaultFolder, FileBefore17, FileTransferTests, SyncProgress, TextBeamTests, NotificationBadgesTests, Running (+4 more)

### Community 7 - "Friends Service"
Cohesion: 0.07
Nodes (24): FriendsService, Friends, Invites, Me, MyActivity, MyTrophies, Now, Pending (+16 more)

### Community 8 - "Encrypted Stream Crypto"
Cohesion: 0.09
Nodes (14): RelayLink, StreamHost, Names, Started, StreamProtocol, Viewer, Channel, Key (+6 more)

### Community 10 - "Beam Text & Tool Clicks"
Cohesion: 0.06
Nodes (3): ShareTarget, Name, PowerInfo

### Community 11 - "Audio Session COM Interfaces"
Cohesion: 0.06
Nodes (6): IAudioSessionControl2, IAudioCaptureClient, IConnector, IPart, IProcess, IPolicyConfig

### Community 12 - "Power, Clipboard & Game Detection"
Cohesion: 0.06
Nodes (5): MonitorEnumProc, Native, POINT, PROCESS_POWER_THROTTLING_STATE, SYSTEM_POWER_STATUS

### Community 13 - "HTTP Client & Fake Handlers"
Cohesion: 0.14
Nodes (4): FakeHandler, UpdateServiceTests, UpdateRelease, UpdateService

### Community 14 - "Updater Install Plan"
Cohesion: 0.08
Nodes (29): Fixture, Directory, Source, Target, TargetHash, Work, UpdateInstallerTests, InstallPlan (+21 more)

### Community 15 - "TCP Routing & Focumon Link"
Cohesion: 0.06
Nodes (20): FocumonLink, Port, Request, Verdict, Allowed, Forbidden, Malformed, TooBig (+12 more)

### Community 19 - "Native Structs & Color Pick"
Cohesion: 0.09
Nodes (5): ColorPick, BITMAPINFOHEADER, ColorPicker, HookProc, MSLLHOOKSTRUCT

### Community 20 - "Settings Menu Items"
Cohesion: 0.04
Nodes (49): MenuAchievementPopups, MenuAi, MenuAnim, MenuAppVolume, MenuAutostart, MenuBeam, MenuBeamText, MenuCalculator (+41 more)

### Community 22 - "Friend Code Cards & ECDH"
Cohesion: 0.08
Nodes (16): CodeCard, K, N, S, T, FriendIdentity, FriendCode, Key (+8 more)

### Community 23 - "Sealed Friend Notes"
Cohesion: 0.21
Nodes (4): FriendNote, ActivityTests, FriendNoteTests, ReactionTests

### Community 24 - "Focumon Handshake Verdict"
Cohesion: 0.10
Nodes (4): FocumonTests, NotificationCenterTests, Enabled, PhoneShareTests

### Community 25 - "Chat Send & Delivery"
Cohesion: 0.12
Nodes (20): ChatState, Failed, Received, Sending, Sent, ChatStore, UnreadTotal, Data (+12 more)

### Community 28 - "Audio Endpoint Devices"
Cohesion: 0.09
Nodes (9): OutputDeviceTests, IMMDevice, IMMDeviceCollection, IMMDeviceEnumerator, OutputDevice, Glyph, PolicyConfigClient, PROPVARIANT (+1 more)

### Community 29 - "Social Context Delegates"
Cohesion: 0.05
Nodes (35): SocialContext, ActivityOf, AddByCode, Animate, Answer, Chats, Face, Friends (+27 more)

### Community 30 - "Per-App Volume Sessions"
Cohesion: 0.08
Nodes (6): AppVolumes, IAudioSessionEnumerator, IAudioSessionManager2, IAudioSessionNotification, ISimpleAudioVolume, OfferBefore17

### Community 31 - "Website Client Script"
Cohesion: 0.13
Nodes (27): applyReleaseFilter(), filter, menu, nav, render(), search, tabs, themes (+19 more)

### Community 32 - "Clipboard History"
Cohesion: 0.13
Nodes (12): ClipboardHistory, Items, ClipEntry, CopiedAt, Height, ImagePath, IsImage, Key (+4 more)

### Community 33 - "Hub Page & Settings Groups"
Cohesion: 0.13
Nodes (10): HubPage, HubView, SettingsGroup, SettingsSwitch, SettingsWindow, HasChanges, Page, ShownView (+2 more)

### Community 34 - "Changelog Features"
Cohesion: 0.08
Nodes (32): Achievements (achievements.json), Active now, Beam (formerly AirDrop), Beam to phone (QR code, local web server), Holm Changelog, Chat with friends, Dynamic Island Windhawk mod, Equalizer that follows the music (WASAPI loopback) (+24 more)

### Community 35 - "Beam Shelf & HTTP Streams"
Cohesion: 0.09
Nodes (6): Shelf, Counter, Bytes, ZeroContent, ShelfTests, UnknownLengthContent

### Community 36 - "Audio Meter Capture"
Cohesion: 0.10
Nodes (6): AudioMeter, Levels, Working, IAudioClient, LevelEnvelope, EqualizerTests

### Community 37 - "Device Change Listener"
Cohesion: 0.09
Nodes (11): Fill, Value, Ear, Radius, Listener, Position, IMMNotificationClient, IPropertyStore (+3 more)

### Community 38 - "Achievement Book"
Cohesion: 0.16
Nodes (9): Achievement, AchievementBook, UnlockedCount, Data, Counters, StreakDay, Unlocked, AchievementsPage (+1 more)

### Community 39 - "USB Drive Detection"
Cohesion: 0.11
Nodes (9): UsbDrivesTests, SP_DEVICE_INTERFACE_DATA, SP_DEVINFO_DATA, STORAGE_DEVICE_NUMBER, UsbDrive, Detail, Name, Root (+1 more)

### Community 40 - "Notch Panels & Pages"
Cohesion: 0.06
Nodes (32): BadgeLogoList, CalculatorPage, ClipboardPage, ClipRows, ClockPanel, CompactFocumon, EqPanel, ExpandedPanel (+24 more)

### Community 41 - "Friend Invites"
Cohesion: 0.14
Nodes (7): Card, C, E, FriendInvite, FriendInviteOnlineTests, FriendInviteTests, Now

### Community 42 - "Nearby LAN Discovery"
Cohesion: 0.12
Nodes (8): NearbyPeer, NearbyService, Id, Incoming, IsRunning, Peers, Packet, NearbyServiceTests

### Community 43 - "File Transfer"
Cohesion: 0.11
Nodes (9): FileDto, FileTransfer, OfferDto, SenderCancelledException, ShareFile, ShareOffer, TotalBytes, RecordingProgress (+1 more)

### Community 44 - "Chat Page UI"
Cohesion: 0.12
Nodes (3): ChatPage, Friend, ChatText

### Community 45 - "Tool Buttons"
Cohesion: 0.06
Nodes (27): AiBtn, BluetoothBtn, CalculatorToolBtn, CallMuteBtn, ClearNotificationsBtn, ClipboardToolBtn, ClipClearBtn, ColorPickerBtn (+19 more)

### Community 48 - "Badge & Art Hosts"
Cohesion: 0.07
Nodes (30): AiIconHost, ArtLayerA, ArtLayerB, BadgePill, BadgePrivacyHost, Bd, BigArt, CallLogoHost (+22 more)

### Community 49 - "Shell Icons & Messenger Logos"
Cohesion: 0.13
Nodes (6): BITMAP, DIBSECTION, IShellItemImageFactory, Shell, ShellApp, SIZE

### Community 50 - "Screen Capture Loop"
Cohesion: 0.15
Nodes (5): BITMAPINFOHEADER, CURSORINFO, ICONINFO, POINT, ScreenCapture

### Community 51 - "Bluetooth Devices"
Cohesion: 0.12
Nodes (10): BtDevice, Glyph, IsConnected, BluetoothControl, BtEntry, IDeviceTopology, IKsControl, KSPROPERTY (+2 more)

### Community 52 - "Send Later Queue"
Cohesion: 0.14
Nodes (12): QueuedSend, AskedUtc, FriendKey, FriendName, Id, NotedUtc, Paths, QueuedUtc (+4 more)

### Community 53 - "Windows Notification Listener"
Cohesion: 0.14
Nodes (6): MessagesService, Access, IsRunning, Messages, UnreadCount, ChatMessage

### Community 55 - "Camera Activity Monitor"
Cohesion: 0.12
Nodes (5): Callback, CameraActivity, ICallback, IMonitor, IReports

### Community 59 - "Repo & Release Conventions"
Cohesion: 0.10
Nodes (19): codex/microsoft-store-msix branch, Repository collaboration preferences, In-app updater, Live microphone/camera monitoring, tools/Measure-Footprint.ps1 and Footprint workflow, Messages from messenger apps, Minimize/Hide over browser tabs, Claude (Anthropic) via Claude Code (+11 more)

### Community 60 - "Release Workflow"
Cohesion: 0.08
Nodes (24): GH_TOKEN, GIPHY_API_KEY, VERSION, version, jobs, release, name, on (+16 more)

### Community 61 - "Notch View Kinds"
Cohesion: 0.08
Nodes (25): Backdrop, BadgeView, CallRow, CallView, CompactView, DownloadRow, DownloadView, ExpandedView (+17 more)

### Community 64 - "Focumon Extension Readers"
Cohesion: 0.18
Nodes (23): absoluteMonsterUrl(), centerOf(), currentTrainerHints(), debug(), elementLooksBlue(), isBlueish(), isVisible(), linkSlug() (+15 more)

### Community 65 - "Timer & Sleep Menus"
Cohesion: 0.16
Nodes (4): SleepTimer, Length, Running, SleepTimerTests

### Community 67 - "Network List Manager COM"
Cohesion: 0.12
Nodes (7): INetworkListManager, IsConnected, IsConnectedToInternet, INetworkListManagerEvents, NetworkWatcher, Online, Sink

### Community 68 - "Calculator Parser"
Cohesion: 0.19
Nodes (4): Calculator, Parser, AtEnd, CalculatorTests

### Community 69 - "Quiet Hours"
Cohesion: 0.16
Nodes (3): QuietNow, QuietHours, QuietHoursTests

### Community 70 - "Friend Note Kinds"
Cohesion: 0.11
Nodes (19): FriendNoteKind, AddedYou, Chat, Files, FriendRequest, FriendRequestCancelled, FriendResponse, Here (+11 more)

### Community 73 - "Footprint Workflow"
Cohesion: 0.09
Nodes (21): VARIANT, variant, jobs, measure, env, runs-on, steps, timeout-minutes (+13 more)

### Community 74 - "Test Workflow"
Cohesion: 0.09
Nodes (21): concurrency, cancel-in-progress, group, jobs, test, name, on, pull_request (+13 more)

### Community 76 - "AI Launcher"
Cohesion: 0.15
Nodes (6): AiLauncher, Choices, AiTarget, Preset, FileIcons, SHFILEINFO

### Community 77 - "Website Build Script"
Cohesion: 0.10
Nodes (15): SITE_URL and BASE_PATH build settings, manifest, origin, out, pages, root, screenshotDirectory, snapshot (+7 more)

### Community 78 - "Folder Pack ZIP"
Cohesion: 0.17
Nodes (5): FolderPack, TempFolder, FolderPackTests, Temp, SyncProgress

### Community 79 - "Bluetooth Battery"
Cohesion: 0.28
Nodes (3): BluetoothBattery, DEVPROPKEY, SP_DEVINFO_DATA

### Community 80 - "Note DTO Fields"
Cohesion: 0.10
Nodes (20): NoteDto, A, C, F, G, I, K, M (+12 more)

### Community 82 - "Countdown Timer"
Cohesion: 0.27
Nodes (4): Countdown, Length, Paused, CountdownTests

### Community 83 - "Website package.json"
Cohesion: 0.11
Nodes (18): prettier, devDependencies, prettier@3.9.10, engines, node, name, private, scripts (+10 more)

### Community 84 - "Reminder Parser"
Cohesion: 0.14
Nodes (7): ReminderParser, TodayTests, IdleStatus, None, Reminder, Shelf, Today

### Community 85 - "Media Info Updates"
Cohesion: 0.24
Nodes (4): MediaInfo, CurrentPosition, MediaUpdateTests, ChangeNames

### Community 86 - "Monitor Enumeration"
Cohesion: 0.15
Nodes (5): MONITORINFO, MONITORINFOEX, RECT, Screen, ColorPickTests

### Community 87 - "Profile Editor"
Cohesion: 0.24
Nodes (3): ProfileDraft, ProfileEditor, HasChanges

### Community 88 - "Release 1.7.0 Tools"
Cohesion: 0.13
Nodes (15): Holm 1.7.0 Release Notes, Beam Text, Caps Lock Indicator, Color Picker, Internet Connection Status, Low Disk Space Warning, Pause Music for Calls, Volume per App Mixer (+7 more)

### Community 89 - "Network Page"
Cohesion: 0.18
Nodes (7): NetworkPage, SpeedPhase, Done, Download, Ping, Upload, SpeedProgress

### Community 90 - "Messenger Detection"
Cohesion: 0.20
Nodes (3): Messenger, Messengers, MessengersTests

### Community 91 - "Reminder List"
Cohesion: 0.24
Nodes (8): Reminder, Due, Id, Text, ReminderList, Items, Next, ReminderListTests

### Community 93 - "Call & Browser Detection"
Cohesion: 0.23
Nodes (6): Browsers, Calls, AllSensors, SensorsInUse, SensorUse, CallsTests

### Community 95 - "App Volume List"
Cohesion: 0.24
Nodes (3): AppVolume, AppVolumeList, AppVolumeTests

### Community 96 - "Friend Profiles"
Cohesion: 0.24
Nodes (3): MyName, FriendProfile, ProfileTests

### Community 98 - "Focumon Phases"
Cohesion: 0.17
Nodes (6): Focumon, FocumonPhase, Break, Focus, Idle, FocumonStatus

### Community 99 - "Screenshot Asset Tests"
Cohesion: 0.14
Nodes (8): setSiteOrigin(), assetRoot, manifest, { base, origin }, files(), pages, root, textByFile

### Community 100 - "Call Pause"
Cohesion: 0.28
Nodes (3): CallPause, Holding, CallPauseTests

### Community 101 - "Notch State Enum"
Cohesion: 0.12
Nodes (16): NotchState, Badge, Call, Download, Expanded, Focumon, Idle, Media (+8 more)

### Community 103 - "Caps Lock Watcher"
Cohesion: 0.20
Nodes (4): CapsLockWatcher, IsOn, HookProc, KBDLLHOOKSTRUCT

### Community 105 - "Mini Slider"
Cohesion: 0.20
Nodes (4): MiniSlider, IsDragging, IsInteractive, Island

### Community 106 - "Weather Kinds"
Cohesion: 0.14
Nodes (12): WeatherKind, Clear, Cloudy, Drizzle, Fog, PartlyCloudy, Rain, Snow (+4 more)

### Community 113 - "App Startup & Mutex"
Cohesion: 0.15
Nodes (9): IsSubmenuOpen, Application, Bd, Check, PART_Popup, App, Border, TextBlock (+1 more)

### Community 114 - "Battery Glyph"
Cohesion: 0.15
Nodes (3): BatteryGlyph, Charging, Level

### Community 115 - "Release Notes: Beam & Status"
Cohesion: 0.18
Nodes (13): Holm 1.5.5 Release Notes, Beam (file sharing), Friends Online Status, Output Device Switching, Debug README Snapshot Mode, VPN Indicator, Send Folders as ZIP, Send Later (+5 more)

### Community 116 - "Release Notes: Social & Settings"
Cohesion: 0.21
Nodes (12): Quiet Hours, Wave at a Friend, Holm 1.7.1 Release Notes, Apply Settings Staging, Profiles, Holm 1.9.0 Release Notes, Achievements, Active Now (+4 more)

### Community 117 - "Clipboard Password Wipe"
Cohesion: 0.35
Nodes (3): ClipboardWipe, Armed, ClipboardWipeTests

### Community 119 - "Badge Counter"
Cohesion: 0.33
Nodes (4): BadgeCounter, Count, Unread, BadgeCounterTests

### Community 120 - "Screen OCR Text"
Cohesion: 0.21
Nodes (4): OcrText, ScreenText, Available, ScreenTextTests

### Community 121 - "Weather Fetching"
Cohesion: 0.19
Nodes (3): Weather, Degrees, RainNote

### Community 122 - "Screenshot Prep Script"
Cohesion: 0.24
Nodes (4): comparison_cutout(), main(), notch_cutout(), window_cutout()

### Community 123 - "Privacy Registry Reads"
Cohesion: 0.20
Nodes (4): Sensor, Camera, Microphone, Watch

### Community 124 - "Microphone Activity"
Cohesion: 0.26
Nodes (3): AudioSessionInfo, MicrophoneActivity, LiveSensorTests

### Community 125 - "Test Project Dependencies"
Cohesion: 0.17
Nodes (10): coverlet.collector (6.0.4), Emoji.Wpf (0.3.4), Microsoft.NET.Test.Sdk (17.14.1), QRCoder (1.7.0), xunit (2.9.3), xunit.runner.visualstudio (3.1.4), net10.0-windows10.0.19041.0, Microsoft.NET.Sdk (+2 more)

### Community 128 - "Release Notes: Today & Weather"
Cohesion: 0.20
Nodes (10): Idle Notch Sky (time of day and weather), Holm 1.11.1 Release Notes, Minimize Over Browser Tabs Pill, Sunny Weather Golden Glow, Notes, Reminders in Plain Words, Today at a Glance, Weather via Open-Meteo (+2 more)

### Community 129 - "Release Notes: Updates & Build"
Cohesion: 0.24
Nodes (10): Holm 1.5.7 Release Notes, Automatic Update Checks (20s after start, every 2h), Wider Open Notch, Update Indicator Arrow in Right-Click Menu, Holm 1.7.3 Release Notes, Dismiss Notifications, Self-Contained Holm.exe Download, One Toolbar in Same Place (+2 more)

### Community 130 - "Release Notes: GIFs & Chat"
Cohesion: 0.18
Nodes (10): Holm 1.9.2 Release Notes, Equalizer That Follows the Music, GIFs via GIPHY, Chat Reactions, Holm 1.9.3 Release Notes, GIF Search Without API Key, Holm 1.9.4 Release Notes, Click Away to Close (+2 more)

### Community 135 - "Website Workflow"
Cohesion: 0.20
Nodes (8): concurrency, cancel-in-progress, group, name, permissions, contents, holmnotch.ch website, Holm website

### Community 136 - "Workflow Steps & Env"
Cohesion: 0.22
Nodes (10): steps, steps, BASE_PATH, GITHUB_TOKEN, SITE_URL, env, id, name (+2 more)

### Community 137 - "Pages Deploy Job"
Cohesion: 0.20
Nodes (10): environment, if, needs, permissions, runs-on, name, url, deploy (+2 more)

### Community 139 - "Release Notes: Privacy Dots"
Cohesion: 0.22
Nodes (7): Holm 1.6.1 Release Notes, Side-by-Side Privacy Dots, Holm 1.7.4 Release Notes, Live Camera Monitoring (Media Foundation), Privacy Dots Inside the Notch, Holm 1.7.5 Release Notes, Camera Indicator Clear Regression Test

### Community 143 - "Focumon Manifest"
Cohesion: 0.22
Nodes (8): background, service_worker, content_scripts, description, host_permissions, manifest_version, name, version

### Community 144 - "Workflow Triggers"
Cohesion: 0.25
Nodes (8): on, pull_request, push, release, paths, branches, paths, types

### Community 145 - "Website Release Data"
Cohesion: 0.25
Nodes (6): Main download selection (newest stable with Holm.exe), data/releases.json, src/releases.mjs, Website verification (npm test, check, lint), destination, API

### Community 146 - "Stream Viewer Window"
Cohesion: 0.32
Nodes (3): StreamViewerWindow, GotFrame, Stopped

### Community 152 - "Holm 1 group"
Cohesion: 0.29
Nodes (4): Holm 1.11.0 Release Notes, Holm Window Memory Release, Spring Open / Glide Close Animation, Holm 1.5.8 Release Notes

### Community 154 - "ClipboardHistory group"
Cohesion: 0.33
Nodes (5): ClipboardPrivacy, ClipVerdict, Keep, Private, Skip

### Community 156 - "Tool group"
Cohesion: 0.29
Nodes (7): Tool, Calculator, Clipboard, Mixer, None, Notes, Reminders

### Community 158 - "defaults group"
Cohesion: 0.33
Nodes (6): defaults, runs-on, run, jobs, build, working-directory

### Community 159 - "default group"
Cohesion: 0.33
Nodes (6): default, description, type, deploy, workflow_dispatch, inputs

### Community 160 - "with group"
Cohesion: 0.33
Nodes (6): with, cache, cache-dependency-path, node-version, path, ref

### Community 162 - "InviteResult group"
Cohesion: 0.33
Nodes (6): InviteResult, Added, AlreadyFriends, NotFound, OwnCode, Sent

### Community 165 - "Autostart group"
Cohesion: 0.33
Nodes (3): Autostart, IsEnabled, FormerName

### Community 166 - "content group"
Cohesion: 0.60
Nodes (3): publish(), snapshot(), start()

### Community 168 - "Holm 1 group"
Cohesion: 0.60
Nodes (3): Holm 1.10.0 Release Notes, Copied Invite Code Notch Offer, Long Friend Code

### Community 169 - "Holm 1 group"
Cohesion: 0.60
Nodes (5): Holm 1.7.6 Release Notes, Focumon in Open Notch, Holm 1.7.7 Release Notes, Focumon for Holm Browser Extension, Focumon in Closed Notch

### Community 170 - "Bar1 group"
Cohesion: 0.40
Nodes (5): Bar1, Bar2, Bar3, Bar4, Rectangle

### Community 171 - "CalcBox group"
Cohesion: 0.40
Nodes (5): CalcBox, NotesBox, ReminderBox, WeatherBox, TextBox

### Community 172 - "AccessState group"
Cohesion: 0.40
Nodes (5): AccessState, Allowed, Denied, Off, Unavailable

### Community 173 - "Clipboard history privacy and  group"
Cohesion: 0.50
Nodes (3): Open-Meteo weather, Sky when nothing plays (time-of-day and weather colours), Today at a glance and notch tools

### Community 174 - " group"
Cohesion: 0.50
Nodes (3): printWidth, singleQuote, trailingComma

### Community 175 - "FriendRequestResult group"
Cohesion: 0.50
Nodes (4): FriendRequestResult, Accepted, Declined, NoResponse

### Community 176 - "DevicesPanel group"
Cohesion: 0.50
Nodes (4): DevicesPanel, ShareTargets, ShelfItemsPanel, WrapPanel

### Community 178 - "Focumon integration in the not group"
Cohesion: 0.67
Nodes (3): Focumon integration in the notch, Focumon for Holm extension, Local endpoint 127.0.0.1:47631

### Community 181 - "CameraDot group"
Cohesion: 0.67
Nodes (3): CameraDot, MicrophoneDot, Ellipse

## Knowledge Gaps
- **458 isolated node(s):** `Device`, `Temp`, `Now`, `Ntfy`, `Relay` (+453 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 764 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **68 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `WPF Animation & Notch State` to `MainWindow XAML Elements`, `Network Details & Routes`, `App Settings Toggles`, `Bluetooth & Caps Lock Menu`, `Friends Service`, `Encrypted Stream Crypto`, `Beam Text & Tool Clicks`, `Power, Clipboard & Game Detection`, `HTTP Client & Fake Handlers`, `TCP Routing & Focumon Link`, `Achievements & Chat Receive`, `WinNotch Namespace Imports`, `xUnit Theory Tests`, `Native Structs & Color Pick`, `Win32 & System Imports`, `Chat Send & Delivery`, `WPF Media & Interop Imports`, `Media Transport Controls`, `Audio Endpoint Devices`, `Social Context Delegates`, `Per-App Volume Sessions`, `Clipboard History`, `Hub Page & Settings Groups`, `Beam Shelf & HTTP Streams`, `Audio Meter Capture`, `Device Change Listener`, `Achievement Book`, `USB Drive Detection`, `Nearby LAN Discovery`, `Tool Buttons`, `Screenshots & Task Pictures`, `Color Picker & Avatars`, `Screen Capture Loop`, `Bluetooth Devices`, `Windows Notification Listener`, `Update Menu Tests`, `Add Friend Page`, `Privacy Monitor`, `MainWindow Init Routines`, `Call, Clock & Sleep Views`, `Updates & System Imports`, `Timer & Sleep Menus`, `Beam to Phone`, `Network List Manager COM`, `Quiet Hours`, `Startup & Footprint Bench`, `Activity Page`, `AI Launcher`, `Countdown Timer`, `Media Info Updates`, `Profile Editor`, `Messenger Detection`, `Reminder List`, `Chat Extras & Emoji`, `Call & Browser Detection`, `Notch Sizing Tests`, `Focumon Phases`, `Call Pause`, `Notch State Enum`, `Message Badge Tests`, `Caps Lock Watcher`, `Click-Away & Reminders`, `Mini Slider`, `Weather Kinds`, `Quiet & AI Menus`, `Download Watcher`, `Disk & Download Events`, `Connection Outage`, `Battery Glyph`, `Clipboard Password Wipe`, `Privacy Registry Reads`, `Send Later Flow`, `Screenshot Watcher`, `Weather Location`, `MainWindow group`, `Tool group`, `MainWindow group`, `IEasingFunction group`?**
  _High betweenness centrality (0.442) - this node is a cross-community bridge._
- **Are the 5 inferred relationships involving `MainWindow` (e.g. with `App.OnStartup()` and `MediaUpdateTests.MusicStoppingTrimsMemorySoon()`) actually correct?**
  _`MainWindow` has 5 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Device`, `Temp`, `Now` to the rest of the system?**
  _458 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `WPF Animation & Notch State` be split into smaller, more focused modules?**
  _Cohesion score 0.029187396351575457 - nodes in this community are weakly interconnected._
- **Why does `Window` connect `MainWindow XAML Elements` to `WPF Animation & Notch State`, `Beam Text & Tool Clicks`, `Stream Viewer Window`, `Native Structs & Color Pick`, `Settings Menu Items`, `Hub Page & Settings Groups`, `Notch Panels & Pages`, `Bar1 group`, `CalcBox group`, `Tool Buttons`, `Screenshots & Task Pictures`, `Badge & Art Hosts`, `DevicesPanel group`, `CameraDot group`, `Cp group`, `MonitorSeparator group`, `PART_ContentHost group`, `PlayIcon group`, `Notch View Kinds`, `Mini Slider`?**
  _High betweenness centrality (0.129) - this node is a cross-community bridge._
- **Should `MainWindow XAML Elements` be split into smaller, more focused modules?**
  _Cohesion score 0.0473111960035263 - nodes in this community are weakly interconnected._
- **Why does `AppSettings` connect `App Settings Toggles` to `WPF Animation & Notch State`, `Disk Space`, `GIF Decoding & Animation`, `Weather Tests`, `Friends Service`, `Wave Rate Limit`, `Notepad`, `Win32 & System Imports`, `Settings Tests`, `Sealed Friend Notes`, `Focumon Handshake Verdict`, `CapsKey group`, `Social Context Delegates`, `Per-App Volume Sessions`, `Clipboard History`, `Hub Page & Settings Groups`, `Browsers group`, `USB Drive Detection`, `Send Later Queue`, `Update Menu Tests`, `Quiet Hours`, `Folder Pack ZIP`, `Monitor Enumeration`, `Reminder List`, `App Volume List`, `Notch Sizing Tests`, `Connection Outage`, `Clipboard Password Wipe`, `Screen OCR Text`?**
  _High betweenness centrality (0.043) - this node is a cross-community bridge._