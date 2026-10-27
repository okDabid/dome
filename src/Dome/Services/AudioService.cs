using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Dome.Services;

// Core Audio controls the actual default multimedia endpoint, not an app-local volume.
public sealed class AudioService : IDisposable
{
    private IAudioEndpointVolume? _endpoint;
    private readonly VolumeCallback _callback;
    private readonly DeviceCallback _deviceCallback;
    private IMMDeviceEnumerator? _enumerator;
    private int _deviceChanged;
    public event Action? Changed;
    public AudioService()
    {
        _callback = new VolumeCallback(() => Changed?.Invoke());
        _deviceCallback = new DeviceCallback(() => { Interlocked.Exchange(ref _deviceChanged, 1); Changed?.Invoke(); });
    }
    public (float Volume, bool Muted) Read()
    {
        Connect();
        Marshal.ThrowExceptionForHR(_endpoint!.GetMasterVolumeLevelScalar(out var volume));
        Marshal.ThrowExceptionForHR(_endpoint.GetMute(out var muted));
        return (volume, muted);
    }
    public void SetVolume(float volume)
    {
        Connect();
        var context = Guid.Empty;
        Marshal.ThrowExceptionForHR(_endpoint!.SetMasterVolumeLevelScalar(Math.Clamp(volume, 0, 1), ref context));
    }
    public void ToggleMute()
    {
        var state = Read();
        var context = Guid.Empty;
        Marshal.ThrowExceptionForHR(_endpoint!.SetMute(!state.Muted, ref context));
    }
    public void Reconnect() { Disconnect(); Connect(); }
    private void Connect()
    {
        if (Interlocked.Exchange(ref _deviceChanged, 0) != 0) Disconnect();
        if (_endpoint != null) return;
        if (_enumerator == null)
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(_deviceCallback));
        }
        IMMDevice? device = null;
        try
        {
            Marshal.ThrowExceptionForHR(_enumerator.GetDefaultAudioEndpoint(0, 1, out device));
            var id = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref id, 23, IntPtr.Zero, out var endpoint));
            _endpoint = (IAudioEndpointVolume)endpoint;
            Marshal.ThrowExceptionForHR(_endpoint.RegisterControlChangeNotify(_callback));
        }
        finally
        {
            if (device != null) Marshal.ReleaseComObject(device);
        }
    }
    private void Disconnect()
    {
        if (_endpoint == null) return;
        _endpoint.UnregisterControlChangeNotify(_callback);
        Marshal.ReleaseComObject(_endpoint);
        _endpoint = null;
    }
    public void Dispose()
    {
        Disconnect();
        if (_enumerator == null) return;
        _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback);
        Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class VolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly Action _changed;
        public VolumeCallback(Action changed) => _changed = changed;
        public int OnNotify(IntPtr data) { _changed(); return 0; }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class DeviceCallback : IMMNotificationClient
    {
        private readonly Action _changed;
        public DeviceCallback(Action changed) => _changed = changed;
        public int OnDeviceStateChanged(string id, uint state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            if (flow == 0 && role == 1) _changed();
            return 0;
        }
        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey { public Guid Format; public uint Id; }
    [ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int state, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient callback);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    }
    [ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback { [PreserveSig] int OnNotify(IntPtr data); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, [In] ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, [In] ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, [In] ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, [In] ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, [In] ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
