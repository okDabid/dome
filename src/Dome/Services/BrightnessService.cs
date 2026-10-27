using System;
using System.Management;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Dome.Services;

public sealed class BrightnessService
{
    // Drivers may block WMI calls; never execute them on the UI thread.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _external;
    private NativePoint _point;
    public async Task<(int? Value, string Source)> ReadAsync(int x, int y)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run<(int? Value, string Source)>(() =>
            {
                _point = new NativePoint { X = x, Y = y };
                var value = ReadDdc(_point);
                _external = value.HasValue;
                if (value.HasValue) return (value, "Selected display · DDC/CI hardware brightness");
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True");
                using var results = searcher.Get();
                foreach (ManagementObject monitor in results)
                    using (monitor) return ((int?)Convert.ToInt32(monitor["CurrentBrightness"]), "Built-in display · Windows display driver");
                return (null, "This display doesn't expose hardware brightness control. Enable DDC/CI in the monitor menu, or use its physical controls.");
            });
        }
        finally { _gate.Release(); }
    }

    public async Task SetAsync(int value)
    {
        var external = _external;
        var point = _point;
        await _gate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (external) { WriteDdc(point, value); return; }
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
                using var results = searcher.Get();
                var changed = false;
                foreach (ManagementObject monitor in results)
                {
                    using (monitor) monitor.InvokeMethod("WmiSetBrightness", new object[] { 0u, (byte)Math.Clamp(value, 0, 100) });
                    changed = true;
                }
                if (!changed) throw new InvalidOperationException("No driver-controlled built-in display was found.");
            });
        }
        finally { _gate.Release(); }
    }

    // Physical-monitor handles are short-lived and always released, including unsupported monitors.
    private static int? ReadDdc(NativePoint point)
    {
        var monitors = GetMonitors(point);
        if (monitors.Length == 0) return null;
        try
        {
            foreach (var monitor in monitors)
                if (GetMonitorBrightness(monitor.Handle, out var min, out var current, out var max) && max > min)
                    return (int)Math.Clamp(((double)current - min) * 100 / (max - min), 0, 100);
            return null;
        }
        finally { DestroyPhysicalMonitors((uint)monitors.Length, monitors); }
    }
    private static void WriteDdc(NativePoint point, int percent)
    {
        var monitors = GetMonitors(point);
        if (monitors.Length == 0) throw new InvalidOperationException("The selected monitor is no longer connected.");
        try
        {
            foreach (var monitor in monitors)
            {
                if (!GetMonitorBrightness(monitor.Handle, out var min, out _, out var max) || max <= min) continue;
                var native = min + (uint)Math.Round(Math.Clamp(percent, 0, 100) / 100d * (max - min));
                if (!SetMonitorBrightness(monitor.Handle, native)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return;
            }
            throw new InvalidOperationException("The monitor no longer accepts DDC/CI brightness control.");
        }
        finally { DestroyPhysicalMonitors((uint)monitors.Length, monitors); }
    }
    private static PhysicalMonitor[] GetMonitors(NativePoint point)
    {
        var logical = MonitorFromPoint(point, 2);
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(logical, out var count) || count == 0 || count > 16) return Array.Empty<PhysicalMonitor>();
        var monitors = new PhysicalMonitor[count];
        if (GetPhysicalMonitorsFromHMONITOR(logical, count, monitors)) return monitors;
        // On partial failure, release any handles the driver did return.
        foreach (var monitor in monitors) if (monitor.Handle != IntPtr.Zero) DestroyPhysicalMonitor(monitor.Handle);
        return Array.Empty<PhysicalMonitor>();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physical);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);
    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint count, [In] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
}
