namespace MiHotKeyApp.Input.Logi;

using System.Globalization;
using System.Windows.Forms;
using MiHotKeyApp.Config;
using MiHotKeyApp.Execution;
using MiHotKeyApp.Input.Wmi;
using MiHotKeyApp.Native;
using Microsoft.Extensions.Logging;

internal sealed class LogiTriggerSource : NativeWindow, ITriggerSource
{
    private readonly ILogger _logger;
    private readonly SessionState _session;
    private readonly object _gate = new();
    private readonly Dictionary<nint, DeviceMeta> _deviceCache = [];

    private Subscription[] _subscriptions = [];
    private bool _started;

    public LogiTriggerSource(ILogger logger, SessionState session)
    {
        _logger = logger;
        _session = session;
        CreateHandle(new CreateParams());
    }

    public event Action<string, string, string>? EventReceived;

    public void SetSubscriptions(IEnumerable<LogiInputConfig> configs)
    {
        lock (_gate)
        {
            _subscriptions = configs.Select(c => new Subscription(c)).ToArray();
            if (_started)
            {
                RegisterUnsafe();
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            _started = true;
            RegisterUnsafe();
            LogDevicesUnsafe();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _started = false;
        }
    }

    public void Dispose()
    {
        Stop();
        DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == RawInput.WM_INPUT)
        {
            HandleRawInput(m.LParam);
        }
        else if (m.Msg == RawInput.WM_INPUT_DEVICE_CHANGE)
        {
            HandleDeviceChange((uint)m.WParam, m.LParam);
        }

        base.WndProc(ref m);
    }

    private void HandleRawInput(nint hRawInput)
    {
        Subscription[] subs;
        lock (_gate)
        {
            if (!_started || _subscriptions.Length == 0)
            {
                return;
            }

            subs = _subscriptions;
        }

        try
        {
            var kb = RawInput.ReadKeyboard(hRawInput);
            if (kb is not null)
            {
                var meta = GetDeviceMeta(kb.Value.DeviceHandle);
                foreach (var sub in subs)
                {
                    if (!sub.Matches(meta, LogiInputKind.Keyboard))
                    {
                        continue;
                    }

                    if (TryMapKeyboard(sub, kb.Value, out var raw, out var mappedEvent))
                    {
                        if (sub.Config.LogRaw)
                        {
                            _logger.LogInformation(
                                "{id} raw keyboard path=\"{path}\" raw={raw}",
                                sub.Config.Id,
                                meta.Name,
                                raw);
                        }

                        EmitIfAllowed(sub, raw, mappedEvent);
                    }
                }

                return;
            }

            var hid = RawInput.ReadHid(hRawInput);
            if (hid is null)
            {
                return;
            }

            var hidMeta = GetDeviceMeta(hid.Value.DeviceHandle);
            var chunkSize = (int)hid.Value.ReportSize;
            if (chunkSize <= 0 || hid.Value.Bytes.Length == 0)
            {
                return;
            }

            for (var offset = 0; offset + chunkSize <= hid.Value.Bytes.Length; offset += chunkSize)
            {
                var report = new byte[chunkSize];
                Buffer.BlockCopy(hid.Value.Bytes, offset, report, 0, chunkSize);
                foreach (var sub in subs)
                {
                    if (!sub.Matches(hidMeta, LogiInputKind.Hid))
                    {
                        continue;
                    }

                    if (TryMapHid(sub, report, out var raw, out var mappedEvent))
                    {
                        if (sub.Config.LogRaw)
                        {
                            _logger.LogInformation(
                                "{id} raw hid path=\"{path}\" usage=0x{usagePage:X4}/0x{usage:X4} raw={raw}",
                                sub.Config.Id,
                                hidMeta.Name,
                                hidMeta.UsagePage ?? 0,
                                hidMeta.Usage ?? 0,
                                raw);
                        }

                        EmitIfAllowed(sub, raw, mappedEvent);
                    }
                    else if (sub.Config.LogRaw)
                    {
                        _logger.LogInformation(
                            "{id} raw hid(unmapped) path=\"{path}\" usage=0x{usagePage:X4}/0x{usage:X4} raw={raw}",
                            sub.Config.Id,
                            hidMeta.Name,
                            hidMeta.UsagePage ?? 0,
                            hidMeta.Usage ?? 0,
                            "hid:hex:" + Convert.ToHexString(report));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logi raw input processing failed");
        }
    }

    private void HandleDeviceChange(uint code, nint deviceHandle)
    {
        lock (_gate)
        {
            _deviceCache.Remove(deviceHandle);
        }

        var meta = GetDeviceMeta(deviceHandle);
        if (!IsLikelyLogitech(meta))
        {
            return;
        }

        var action = code == RawInput.GIDC_ARRIVAL ? "arrival" :
            code == RawInput.GIDC_REMOVAL ? "removal" : $"change({code})";

        _logger.LogInformation(
            "raw-device {action} type={type} vid={vid} pid={pid} usage=0x{usagePage:X4}/0x{usage:X4} path=\"{path}\"",
            action,
            meta.Type,
            meta.VendorId?.ToString("X4", CultureInfo.InvariantCulture) ?? "",
            meta.ProductId?.ToString("X4", CultureInfo.InvariantCulture) ?? "",
            meta.UsagePage ?? 0,
            meta.Usage ?? 0,
            meta.Name);
    }

    private void RegisterUnsafe()
    {
        var usages = new HashSet<(ushort, ushort)>
        {
            (0x01, 0x06), // Keyboard
            (0x01, 0x80), // System Control
            (0x0C, 0x01), // Consumer Control
        };

        foreach (var sub in _subscriptions)
        {
            if (sub.UsagePage is ushort page && sub.Usage is ushort usage)
            {
                usages.Add((page, usage));
            }
        }

        if (!RawInput.Register(Handle, usages))
        {
            _logger.LogWarning("RawInput register failed for logi source");
            return;
        }

        _logger.LogInformation("logi raw-input registered usages={count}", usages.Count);
    }

    private void LogDevicesUnsafe()
    {
        var devices = RawInput.EnumerateDevices()
            .Where(IsLikelyLogitech)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (devices.Length == 0)
        {
            _logger.LogInformation("logi raw-input devices: none found");
            return;
        }

        _logger.LogInformation("logi raw-input devices count={count}", devices.Length);
        foreach (var d in devices)
        {
            _logger.LogInformation(
                "logi device type={type} vid={vid} pid={pid} usage=0x{usagePage:X4}/0x{usage:X4} path=\"{path}\"",
                d.Type,
                d.VendorId?.ToString("X4", CultureInfo.InvariantCulture) ?? "",
                d.ProductId?.ToString("X4", CultureInfo.InvariantCulture) ?? "",
                d.UsagePage ?? 0,
                d.Usage ?? 0,
                d.Name);
        }
    }

    private DeviceMeta GetDeviceMeta(nint deviceHandle)
    {
        lock (_gate)
        {
            if (_deviceCache.TryGetValue(deviceHandle, out var cached))
            {
                return cached;
            }
        }

        var info = RawInput.GetDeviceInfo(deviceHandle);
        var meta = DeviceMeta.From(info);

        lock (_gate)
        {
            _deviceCache[deviceHandle] = meta;
        }

        return meta;
    }

    private void EmitIfAllowed(Subscription sub, string raw, string mappedEvent)
    {
        if (sub.Config.RepeatHandling.Equals("firstDownOnlyUntilUp", StringComparison.OrdinalIgnoreCase) &&
            !sub.RepeatGate.ShouldAccept(mappedEvent))
        {
            return;
        }

        var policy = sub.Config.SessionPolicy;
        if (sub.Config.SessionPolicyByEvent.TryGetValue(mappedEvent, out var perEvent))
        {
            policy = perEvent;
        }

        if (!IsAllowed(policy))
        {
            _logger.LogDebug(
                "drop src={src} raw={raw} event={event} policy={policy} locked={locked} remote={remote}",
                sub.Config.Id,
                raw,
                mappedEvent,
                policy,
                _session.IsLocked ? 1 : 0,
                _session.IsRemoteSession ? 1 : 0);
            return;
        }

        EventReceived?.Invoke(sub.Config.Id, raw, mappedEvent);
    }

    private bool IsAllowed(InputSessionPolicy policy)
    {
        return policy switch
        {
            InputSessionPolicy.Any => true,
            InputSessionPolicy.RequireUnlocked => !_session.IsLocked,
            InputSessionPolicy.RequireLocalSession => !_session.IsRemoteSession,
            InputSessionPolicy.RequireUnlockedLocalSession => !_session.IsLocked && !_session.IsRemoteSession,
            _ => true,
        };
    }

    private static bool TryMapKeyboard(Subscription sub, RawInput.RawKeyboardData kb, out string raw, out string mappedEvent)
    {
        var suffix = kb.IsKeyUp ? ".up" : ".down";
        var candidates = new List<string>(6)
        {
            $"kbd:vk:{kb.VKey}{suffix}",
            $"kbd:vk:0x{kb.VKey:X2}{suffix}",
        };

        if (kb.MakeCode != 0)
        {
            candidates.Add($"kbd:scan:{kb.MakeCode}{suffix}");
            candidates.Add($"kbd:scan:0x{kb.MakeCode:X2}{suffix}");
            if (kb.IsE0)
            {
                candidates.Add($"kbd:scan:0x{kb.MakeCode:X2}.e0{suffix}");
            }
            if (kb.IsE1)
            {
                candidates.Add($"kbd:scan:0x{kb.MakeCode:X2}.e1{suffix}");
            }
        }

        raw = candidates.First();
        return TryMapCandidates(sub.Config.Map, candidates, out mappedEvent);
    }

    private static bool TryMapHid(Subscription sub, byte[] report, out string raw, out string mappedEvent)
    {
        var hex = Convert.ToHexString(report);
        var candidates = new[]
        {
            "hid:hex:" + hex,
            "hid:len:" + report.Length + ":" + hex,
        };

        raw = candidates[0];
        return TryMapCandidates(sub.Config.Map, candidates, out mappedEvent);
    }

    private static bool TryMapCandidates(
        Dictionary<string, string> map,
        IEnumerable<string> candidates,
        out string mappedEvent)
    {
        foreach (var (pattern, ev) in map)
        {
            foreach (var candidate in candidates)
            {
                if (MatchesPattern(pattern, candidate))
                {
                    mappedEvent = ev;
                    return true;
                }
            }
        }

        mappedEvent = "";
        return false;
    }

    private static bool MatchesPattern(string pattern, string candidate)
    {
        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(pattern, candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyLogitech(RawInput.RawInputDeviceInfo info)
    {
        if (info.VendorId == 0x046D)
        {
            return true;
        }

        return info.Name.Contains("VID_046D", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyLogitech(DeviceMeta meta)
    {
        if (meta.VendorId == 0x046D)
        {
            return true;
        }

        return meta.Name.Contains("VID_046D", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Subscription
    {
        public Subscription(LogiInputConfig config)
        {
            Config = config;
            RepeatGate = new RepeatGate(config.DebounceMs);

            VendorId = TryParseHexWord(config.VendorId);
            ProductId = TryParseHexWord(config.ProductId);
            UsagePage = config.UsagePage is >= 0 and <= 0xFFFF ? (ushort)config.UsagePage.Value : null;
            Usage = config.Usage is >= 0 and <= 0xFFFF ? (ushort)config.Usage.Value : null;
        }

        public LogiInputConfig Config { get; }
        public RepeatGate RepeatGate { get; }
        public ushort? VendorId { get; }
        public ushort? ProductId { get; }
        public ushort? UsagePage { get; }
        public ushort? Usage { get; }

        public bool Matches(DeviceMeta meta, LogiInputKind eventKind)
        {
            if (Config.Kind != LogiInputKind.Any && Config.Kind != eventKind)
            {
                return false;
            }

            if (VendorId is ushort vid && meta.VendorId != vid)
            {
                return false;
            }

            if (ProductId is ushort pid && meta.ProductId != pid)
            {
                return false;
            }

            if (UsagePage is ushort page && meta.UsagePage != page)
            {
                return false;
            }

            if (Usage is ushort usage && meta.Usage != usage)
            {
                return false;
            }

            if (Config.DevicePathContains.Length > 0)
            {
                var matched = false;
                foreach (var part in Config.DevicePathContains)
                {
                    if (string.IsNullOrWhiteSpace(part))
                    {
                        continue;
                    }

                    if (meta.Name.Contains(part, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return false;
                }
            }

            return true;
        }

        private static ushort? TryParseHexWord(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var s = value.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                s = s[2..];
            }

            return ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }
    }

    private sealed record DeviceMeta(
        nint Handle,
        uint Type,
        string Name,
        ushort? VendorId,
        ushort? ProductId,
        ushort? UsagePage,
        ushort? Usage)
    {
        public static DeviceMeta From(RawInput.RawInputDeviceInfo info)
        {
            var (pathVid, pathPid) = ParseVidPid(info.Name);

            return new DeviceMeta(
                info.Handle,
                info.Type,
                info.Name,
                (ushort?)info.VendorId ?? pathVid,
                (ushort?)info.ProductId ?? pathPid,
                info.UsagePage,
                info.Usage);
        }

        private static (ushort? Vid, ushort? Pid) ParseVidPid(string path)
        {
            return (TryParsePathHex(path, "VID_"), TryParsePathHex(path, "PID_"));
        }

        private static ushort? TryParsePathHex(string value, string marker)
        {
            var ix = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (ix < 0 || ix + marker.Length + 4 > value.Length)
            {
                return null;
            }

            var hex = value.Substring(ix + marker.Length, 4);
            return ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }
    }
}
