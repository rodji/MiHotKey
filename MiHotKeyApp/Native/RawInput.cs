namespace MiHotKeyApp.Native;

using System.Runtime.InteropServices;
using System.Text;

internal static class RawInput
{
    public const int WM_INPUT = 0x00FF;
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;

    public const uint GIDC_ARRIVAL = 1;
    public const uint GIDC_REMOVAL = 2;

    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEHID = 2;

    public const uint RID_INPUT = 0x10000003;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000B;

    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_DEVNOTIFY = 0x00002000;

    public const ushort RI_KEY_BREAK = 0x0001;
    public const ushort RI_KEY_E0 = 0x0002;
    public const ushort RI_KEY_E1 = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public nint hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICELIST
    {
        public nint hDevice;
        public uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public nint hDevice;
        public nint wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTKEYBOARD
    {
        public RAWINPUTHEADER header;
        public RAWKEYBOARD keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHID
    {
        public RAWINPUTHEADER header;
        public uint dwSizeHid;
        public uint dwCount;
        public byte bRawData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RID_DEVICE_INFO
    {
        public uint cbSize;
        public uint dwType;
        public RID_DEVICE_INFO_UNION info;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct RID_DEVICE_INFO_UNION
    {
        [FieldOffset(0)]
        public RID_DEVICE_INFO_MOUSE mouse;

        [FieldOffset(0)]
        public RID_DEVICE_INFO_KEYBOARD keyboard;

        [FieldOffset(0)]
        public RID_DEVICE_INFO_HID hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RID_DEVICE_INFO_MOUSE
    {
        public uint dwId;
        public uint dwNumberOfButtons;
        public uint dwSampleRate;
        public bool fHasHorizontalWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RID_DEVICE_INFO_KEYBOARD
    {
        public uint dwType;
        public uint dwSubType;
        public uint dwKeyboardMode;
        public uint dwNumberOfFunctionKeys;
        public uint dwNumberOfIndicators;
        public uint dwNumberOfKeysTotal;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RID_DEVICE_INFO_HID
    {
        public uint dwVendorId;
        public uint dwProductId;
        public uint dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    internal readonly record struct RawInputDeviceInfo(
        nint Handle,
        uint Type,
        string Name,
        uint? VendorId,
        uint? ProductId,
        ushort? UsagePage,
        ushort? Usage);

    internal readonly record struct RawKeyboardData(
        nint DeviceHandle,
        ushort VKey,
        ushort MakeCode,
        bool IsKeyUp,
        bool IsE0,
        bool IsE1);

    internal readonly record struct RawHidData(
        nint DeviceHandle,
        byte[] Bytes,
        uint ReportSize,
        uint ReportCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        nint hRawInput,
        uint uiCommand,
        nint pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        nint pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfoBuffer(
        nint hDevice,
        uint uiCommand,
        nint pData,
        ref uint pcbSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfoString(
        nint hDevice,
        uint uiCommand,
        StringBuilder pData,
        ref uint pcbSize);

    internal static bool Register(nint hwndTarget, IEnumerable<(ushort UsagePage, ushort Usage)> registrations)
    {
        var list = registrations
            .Distinct()
            .Select(x => new RAWINPUTDEVICE
            {
                usUsagePage = x.UsagePage,
                usUsage = x.Usage,
                dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                hwndTarget = hwndTarget,
            })
            .ToArray();

        if (list.Length == 0)
        {
            return true;
        }

        return RegisterRawInputDevices(list, (uint)list.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    internal static RawKeyboardData? ReadKeyboard(nint hRawInput)
    {
        if (!TryReadRawInput(hRawInput, out var ptr, out _))
        {
            return null;
        }

        try
        {
            var data = Marshal.PtrToStructure<RAWINPUTKEYBOARD>(ptr);
            if (data.header.dwType != RIM_TYPEKEYBOARD)
            {
                return null;
            }

            var flags = data.keyboard.Flags;
            return new RawKeyboardData(
                data.header.hDevice,
                data.keyboard.VKey,
                data.keyboard.MakeCode,
                (flags & RI_KEY_BREAK) != 0,
                (flags & RI_KEY_E0) != 0,
                (flags & RI_KEY_E1) != 0);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    internal static RawHidData? ReadHid(nint hRawInput)
    {
        if (!TryReadRawInput(hRawInput, out var ptr, out var size))
        {
            return null;
        }

        try
        {
            var data = Marshal.PtrToStructure<RAWINPUTHID>(ptr);
            if (data.header.dwType != RIM_TYPEHID)
            {
                return null;
            }

            var rawOffset = Marshal.OffsetOf<RAWINPUTHID>(nameof(RAWINPUTHID.bRawData)).ToInt32();
            var total = checked((int)(data.dwSizeHid * data.dwCount));
            if (total < 0 || rawOffset + total > size)
            {
                return null;
            }

            var bytes = new byte[total];
            Marshal.Copy(nint.Add(ptr, rawOffset), bytes, 0, total);
            return new RawHidData(data.header.hDevice, bytes, data.dwSizeHid, data.dwCount);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    internal static RawInputDeviceInfo GetDeviceInfo(nint deviceHandle)
    {
        var name = GetDeviceName(deviceHandle);
        if (!TryGetDeviceInfoStruct(deviceHandle, out var info))
        {
            return new RawInputDeviceInfo(deviceHandle, uint.MaxValue, name, null, null, null, null);
        }

        return info.dwType switch
        {
            RIM_TYPEHID => new RawInputDeviceInfo(
                deviceHandle,
                info.dwType,
                name,
                info.info.hid.dwVendorId,
                info.info.hid.dwProductId,
                info.info.hid.usUsagePage,
                info.info.hid.usUsage),
            _ => new RawInputDeviceInfo(deviceHandle, info.dwType, name, null, null, null, null),
        };
    }

    internal static RawInputDeviceInfo[] EnumerateDevices()
    {
        var count = 0u;
        var rc = GetRawInputDeviceList(0, ref count, (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>());
        if (rc == uint.MaxValue || count == 0)
        {
            return [];
        }

        var itemSize = Marshal.SizeOf<RAWINPUTDEVICELIST>();
        var totalBytes = checked((int)(count * (uint)itemSize));
        var ptr = Marshal.AllocHGlobal(totalBytes);
        try
        {
            var actual = count;
            rc = GetRawInputDeviceList(ptr, ref actual, (uint)itemSize);
            if (rc == uint.MaxValue)
            {
                return [];
            }

            var list = new RawInputDeviceInfo[actual];
            for (var i = 0; i < actual; i++)
            {
                var itemPtr = nint.Add(ptr, i * itemSize);
                var item = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(itemPtr);
                list[i] = GetDeviceInfo(item.hDevice);
            }

            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static bool TryReadRawInput(nint hRawInput, out nint ptr, out int size)
    {
        ptr = 0;
        size = 0;

        var cb = 0u;
        var rc = GetRawInputData(hRawInput, RID_INPUT, 0, ref cb, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (rc == uint.MaxValue || cb == 0)
        {
            return false;
        }

        size = checked((int)cb);
        ptr = Marshal.AllocHGlobal(size);
        rc = GetRawInputData(hRawInput, RID_INPUT, ptr, ref cb, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (rc == uint.MaxValue)
        {
            Marshal.FreeHGlobal(ptr);
            ptr = 0;
            size = 0;
            return false;
        }

        return true;
    }

    private static string GetDeviceName(nint deviceHandle)
    {
        var chars = 0u;
        var rc = GetRawInputDeviceInfoBuffer(deviceHandle, RIDI_DEVICENAME, 0, ref chars);
        if (rc == uint.MaxValue || chars == 0)
        {
            return "";
        }

        var sb = new StringBuilder((int)chars);
        rc = GetRawInputDeviceInfoString(deviceHandle, RIDI_DEVICENAME, sb, ref chars);
        if (rc == uint.MaxValue)
        {
            return "";
        }

        return sb.ToString();
    }

    private static bool TryGetDeviceInfoStruct(nint deviceHandle, out RID_DEVICE_INFO info)
    {
        info = new RID_DEVICE_INFO
        {
            cbSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>(),
        };

        var cb = info.cbSize;
        var ptr = Marshal.AllocHGlobal((int)cb);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            var rc = GetRawInputDeviceInfoBuffer(deviceHandle, RIDI_DEVICEINFO, ptr, ref cb);
            if (rc == uint.MaxValue)
            {
                info = default;
                return false;
            }

            info = Marshal.PtrToStructure<RID_DEVICE_INFO>(ptr);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
