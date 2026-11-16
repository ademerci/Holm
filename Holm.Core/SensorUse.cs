using System;

namespace WinNotch;

internal enum Sensor { Microphone, Camera }

/// <summary>An app using the microphone or the camera right now.</summary>
/// <param name="Key">Its process name ("Discord") or, for a Store app, its package ("5319275A.WhatsAppDesktop_cv1g1gvanyjgm").</param>
/// <param name="Since">When it started, UTC.</param>
internal sealed record SensorUse(Sensor Sensor, string Name, string Key, DateTime Since);

/// <summary>One app's entry in Windows' record of who used a sensor. Times are FILETIMEs; Stop is 0 while it is in use.</summary>
internal readonly record struct SensorEntry(string Name, bool NonPackaged, long Start, long Stop);
