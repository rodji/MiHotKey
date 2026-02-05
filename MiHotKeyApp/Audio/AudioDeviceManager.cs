namespace MiHotKeyApp.Audio;

using System.Runtime.InteropServices;
using MiHotKeyApp.Config;
using Microsoft.Extensions.Logging;

internal sealed class AudioDeviceManager
{
    private readonly ILogger _logger;

    public AudioDeviceManager(ILogger logger)
    {
        _logger = logger;
    }

    public AudioDeviceInfo[] GetDevicesSnapshot()
    {
        var list = new List<AudioDeviceInfo>();

        var enumerator = CreateEnumerator();
        if (enumerator is null)
        {
            return list.ToArray();
        }

        try
        {
            AddDevicesForFlow(enumerator, EDataFlow.eCapture, AudioFlow.Capture, list);
            AddDevicesForFlow(enumerator, EDataFlow.eRender, AudioFlow.Render, list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "audio diagnostics failed");
        }
        finally
        {
            Release(enumerator);
        }

        return list.ToArray();
    }

    public bool Execute(string actionId, AudioDeviceConfig cfg, string? context)
    {
        try
        {
            var flow = ToDataFlow(cfg.Flow);
            var role = ToRole(cfg.Role);

            var deviceId = string.IsNullOrWhiteSpace(cfg.DeviceId) ? "<default>" : cfg.DeviceId;

            if (!TryGetDevice(flow, role, cfg.DeviceId, out var device))
            {
                _logger.LogWarning("audio device not found id={id} flow={flow} role={role} ctx=\"{ctx}\"", deviceId, cfg.Flow, cfg.Role, context ?? "");
                return false;
            }

            if (!TryGetEndpointVolume(device!, out var volume))
            {
                _logger.LogWarning("audio endpoint volume not available id={id} flow={flow} role={role} ctx=\"{ctx}\"", deviceId, cfg.Flow, cfg.Role, context ?? "");
                Release(device);
                return false;
            }

            try
            {
                if (volume.GetMute(out var isMuted) != 0)
                {
                    _logger.LogWarning("audio get mute failed id={id} ctx=\"{ctx}\"", deviceId, context ?? "");
                    return false;
                }

                var target = cfg.Action switch
                {
                    AudioAction.ToggleMute => !isMuted,
                    AudioAction.Mute => true,
                    AudioAction.Unmute => false,
                    _ => !isMuted,
                };

                if (volume.SetMute(target, Guid.Empty) != 0)
                {
                    _logger.LogWarning("audio set mute failed id={id} target={target} ctx=\"{ctx}\"", deviceId, target ? 1 : 0, context ?? "");
                    return false;
                }

                _logger.LogInformation(
                    "audio mute id={id} action={action} before={before} after={after} flow={flow} role={role} ctx=\"{ctx}\"",
                    deviceId,
                    cfg.Action,
                    isMuted ? 1 : 0,
                    target ? 1 : 0,
                    cfg.Flow,
                    cfg.Role,
                    context ?? "");

                return true;
            }
            finally
            {
                Release(volume);
                Release(device);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "audio execute failed id={id} ctx=\"{ctx}\"", actionId, context ?? "");
            return false;
        }
    }

    private static bool TryGetDevice(EDataFlow flow, ERole role, string deviceId, out IMMDevice? device)
    {
        device = null;
        var enumerator = CreateEnumerator();
        if (enumerator is null)
        {
            return false;
        }

        try
        {
            var hr = string.IsNullOrWhiteSpace(deviceId)
                ? enumerator.GetDefaultAudioEndpoint(flow, role, out device)
                : enumerator.GetDevice(deviceId, out device);

            return hr == 0 && device is not null;
        }
        finally
        {
            Release(enumerator);
        }
    }

    private static bool TryGetEndpointVolume(IMMDevice device, out IAudioEndpointVolume volume)
    {
        volume = null!;
        var iid = typeof(IAudioEndpointVolume).GUID;
        var hr = device.Activate(ref iid, CLSCTX.ALL, IntPtr.Zero, out var obj);
        if (hr != 0 || obj is not IAudioEndpointVolume ep)
        {
            return false;
        }

        volume = ep;
        return true;
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    private static EDataFlow ToDataFlow(AudioFlow flow)
    {
        return flow switch
        {
            AudioFlow.Render => EDataFlow.eRender,
            AudioFlow.Capture => EDataFlow.eCapture,
            _ => EDataFlow.eCapture,
        };
    }

    private static ERole ToRole(AudioRole role)
    {
        return role switch
        {
            AudioRole.Console => ERole.eConsole,
            AudioRole.Multimedia => ERole.eMultimedia,
            AudioRole.Communications => ERole.eCommunications,
            _ => ERole.eCommunications,
        };
    }

    private enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2,
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2,
    }

    [Flags]
    private enum CLSCTX : uint
    {
        INPROC_SERVER = 0x1,
        INPROC_HANDLER = 0x2,
        LOCAL_SERVER = 0x4,
        REMOTE_SERVER = 0x10,
        ALL = INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER,
    }

    private const uint DEVICE_STATE_ACTIVE = 0x00000001;
    private const int STGM_READ = 0;

    private static IMMDeviceEnumerator? CreateEnumerator()
    {
        var type = Type.GetTypeFromCLSID(typeof(MMDeviceEnumerator).GUID);
        if (type is null)
        {
            return null;
        }

        return (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
    }

    private static void AddDevicesForFlow(IMMDeviceEnumerator enumerator, EDataFlow flow, AudioFlow mappedFlow, List<AudioDeviceInfo> list)
    {
        if (enumerator.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out var collection) != 0 || collection is null)
        {
            return;
        }

        var defaultConsole = TryGetDefaultId(enumerator, flow, ERole.eConsole);
        var defaultMultimedia = TryGetDefaultId(enumerator, flow, ERole.eMultimedia);
        var defaultComms = TryGetDefaultId(enumerator, flow, ERole.eCommunications);

        try
        {
            if (collection.GetCount(out var count) != 0)
            {
                return;
            }

            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0 || device is null)
                {
                    continue;
                }

                try
                {
                    if (device.GetId(out var id) != 0)
                    {
                        continue;
                    }

                    var name = TryGetDeviceName(device) ?? "";
                    list.Add(new AudioDeviceInfo(
                        Id: id,
                        Name: name,
                        Flow: mappedFlow,
                        IsDefaultConsole: string.Equals(id, defaultConsole, StringComparison.OrdinalIgnoreCase),
                        IsDefaultMultimedia: string.Equals(id, defaultMultimedia, StringComparison.OrdinalIgnoreCase),
                        IsDefaultCommunications: string.Equals(id, defaultComms, StringComparison.OrdinalIgnoreCase)));
                }
                finally
                {
                    Release(device);
                }
            }
        }
        finally
        {
            Release(collection);
        }
    }

    private static string? TryGetDefaultId(IMMDeviceEnumerator enumerator, EDataFlow flow, ERole role)
    {
        if (enumerator.GetDefaultAudioEndpoint(flow, role, out var device) != 0 || device is null)
        {
            return null;
        }

        try
        {
            return device.GetId(out var id) == 0 ? id : null;
        }
        finally
        {
            Release(device);
        }
    }

    private static string? TryGetDeviceName(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0 || store is null)
        {
            return null;
        }

        try
        {
            var key = PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out var pv) != 0)
            {
                return null;
            }

            try
            {
                return pv.GetString();
            }
            finally
            {
                pv.Clear();
            }
        }
        finally
        {
            Release(store);
        }
    }

    internal sealed record AudioDeviceInfo(
        string Id,
        string Name,
        AudioFlow Flow,
        bool IsDefaultConsole,
        bool IsDefaultMultimedia,
        bool IsDefaultCommunications);

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0A0B9B3E1F9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint pcDevices);
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? ppInterface);
        int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out uint pdwState);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PropertyKey pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, ref PropVariant pv);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    private static readonly PropertyKey PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14,
    };

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public IntPtr ptr;

        public string? GetString()
        {
            const ushort VT_LPWSTR = 31;
            const ushort VT_BSTR = 8;

            if (vt == VT_LPWSTR)
            {
                return Marshal.PtrToStringUni(ptr);
            }

            if (vt == VT_BSTR)
            {
                return Marshal.PtrToStringBSTR(ptr);
            }

            return null;
        }

        public void Clear()
        {
            _ = PropVariantClear(ref this);
        }
    }

    [DllImport("Ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);
        int GetMute(out bool pbMute);
        int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        int VolumeStepUp(Guid pguidEventContext);
        int VolumeStepDown(Guid pguidEventContext);
        int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }
}
