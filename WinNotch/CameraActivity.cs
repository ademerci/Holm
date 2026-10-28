using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace WinNotch;

/// <summary>Media Foundation camera activity reports. Observes streams without opening a camera or reading frames.</summary>
internal sealed class CameraActivity : IDisposable
{
    [ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMonitor
    {
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
    }
    [ComImport, Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICallback
    {
        [PreserveSig] int OnActivitiesReport(IReports reports);
    }
    [ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IReports
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetActivityReport(uint index, out IReport report);
        [PreserveSig] int GetActivityReportByDeviceName([MarshalAs(UnmanagedType.LPWStr)] string name, out IReport report);
    }
    [ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IReport
    {
        [PreserveSig] int GetFriendlyName([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint size, out uint written);
        [PreserveSig] int GetSymbolicLink([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint size, out uint written);
        [PreserveSig] int GetProcessCount(out uint count);
        [PreserveSig] int GetProcessActivity(uint index, out IProcess process);
    }
    [ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IProcess
    {
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int GetStreamingState([MarshalAs(UnmanagedType.Bool)] out bool streaming);
        [PreserveSig] int GetStreamingMode(out int mode);
        [PreserveSig] int GetReportTime(out long time);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class Callback : ICallback
    {
        readonly CameraActivity _owner;
        public Callback(CameraActivity owner) => _owner = owner;
        public int OnActivitiesReport(IReports reports)
        {
            try { _owner.Read(reports); }
            catch (Exception ex) { Diag.Log("Camera activity: " + ex.GetType().Name); }
            return 0;
        }
    }
    [DllImport("mfplat.dll")] static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] static extern int MFShutdown();
    [DllImport("mfsensorgroup.dll")] static extern int MFCreateSensorActivityMonitor(ICallback callback, out IMonitor monitor);

    readonly object _gate = new();
    readonly Dictionary<string, IReadOnlyList<SensorUse>> _devices = new(StringComparer.OrdinalIgnoreCase);
    readonly Callback _callback;
    IMonitor? _monitor;
    bool _disposed;
    public event Action<IReadOnlyList<SensorUse>>? Changed;

    public CameraActivity()
    {
        _callback = new Callback(this);
        Marshal.ThrowExceptionForHR(MFStartup(0x20070, 0));
        try { Marshal.ThrowExceptionForHR(MFCreateSensorActivityMonitor(_callback, out _monitor)); }
        catch { MFShutdown(); throw; }
    }

    public void Start() => Marshal.ThrowExceptionForHR(_monitor!.Start());

    void Read(IReports reports)
    {
        IReadOnlyList<SensorUse> current;
        lock (_gate)
        {
            if (_disposed || reports.GetCount(out uint count) != 0) return;
            for (uint i = 0; i < count; i++)
            {
                if (reports.GetActivityReport(i, out var report) != 0) continue;
                try
                {
                    var link = new StringBuilder(2048);
                    if (report.GetSymbolicLink(link, (uint)link.Capacity, out _) != 0 || report.GetProcessCount(out uint processes) != 0) continue;
                    var active = new List<SensorUse>();
                    for (uint j = 0; j < processes; j++)
                    {
                        if (report.GetProcessActivity(j, out var process) != 0) continue;
                        try
                        {
                            if (process.GetStreamingState(out bool streaming) != 0 || !streaming || process.GetProcessId(out uint pid) != 0) continue;
                            string key = Path.GetFileNameWithoutExtension(Native.ProcessPath((int)pid));
                            if (key.Length > 0) active.Add(new SensorUse(Sensor.Camera, PrivacyMonitor.NameOfProcess(key), key, DateTime.UtcNow));
                            else active.Add(new SensorUse(Sensor.Camera, "Camera", "camera-pid:" + pid, DateTime.UtcNow));
                        }
                        finally { Marshal.ReleaseComObject(process); }
                    }
                    _devices[link.ToString()] = active;
                }
                finally { Marshal.ReleaseComObject(report); }
            }
            current = _devices.Values.SelectMany(uses => uses).DistinctBy(use => use.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        Changed?.Invoke(current);
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        if (_monitor != null) { _monitor.Stop(); Marshal.ReleaseComObject(_monitor); _monitor = null; }
        MFShutdown();
    }
}
