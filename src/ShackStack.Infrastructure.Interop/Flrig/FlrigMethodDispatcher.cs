using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Interop.Flrig;

public sealed class FlrigMethodDispatcher
{
    private const string FakeVersion = "2.0.4";
    private const string FakeRadio = "IC-7300";
    private readonly object _gate = new();
    private readonly SimpleSubject<InteropEvent> _events = new();
    private RadioState _state = new(false, 14_200_000, 14_200_000, 14_205_000, RadioMode.Usb, false, false, false, 0, 2, 2400, 0, 0, false, false, false, 0, false, false, 1, 128, false, false, 50, 0, 100, 700, 20);
    private Func<long, CancellationToken, Task>? _setFrequencyAsync;
    private Func<RadioMode, CancellationToken, Task>? _setModeAsync;
    private Func<bool, CancellationToken, Task>? _setPttAsync;

    public IObservable<InteropEvent> Events => _events;

    public void UpdateRadioState(RadioState state)
    {
        lock (_gate)
        {
            _state = state;
        }
    }

    public void ConfigureControlHandlers(
        Func<long, CancellationToken, Task> setFrequencyAsync,
        Func<RadioMode, CancellationToken, Task> setModeAsync,
        Func<bool, CancellationToken, Task> setPttAsync)
    {
        _setFrequencyAsync = setFrequencyAsync;
        _setModeAsync = setModeAsync;
        _setPttAsync = setPttAsync;
    }

    public object Dispatch(XmlRpcRequest request)
    {
        object result = request.MethodName switch
        {
            "main.get_version" => FakeVersion,
            "rig.get_xcvr" => FakeRadio,
            "rig.get_info" => GetInfo(),
            "rig.get_vfo" => GetVfo(),
            "rig.get_AB" => GetVfo(),
            "rig.set_vfo" => SetVfo(request),
            "main.set_frequency" => SetVfo(request),
            "main.get_frequency" => GetVfo(),
            "rig.set_AB" => SetAb(request),
            "rig.get_mode" => GetMode(),
            "rig.get_modeA" => GetMode(),
            "rig.get_modes" => GetModes(),
            "rig.set_mode" => SetMode(request),
            "rig.get_ptt" => GetPtt(),
            "rig.set_ptt" => SetPtt(request),
            "rig.get_smeter" => GetSmeter(),
            "rig.get_power" => 100,
            "rig.get_pwrlevel" => 100.0,
            "rig.get_pwrmeter" => "0",
            "rig.get_pwrmax" => "100",
            "rig.get_swrmeter" => "1.0",
            "rig.get_bw" => GetBandwidth(),
            "rig.get_bws" => GetBandwidths(),
            "rig.get_split" => 0,
            "rig.get_notch" => 0,
            "rig.get_sideband" => GetSideband(),
            "main.get_trx_state" => GetTrxState(),
            "rig.set_pwrlevel" => 0,
            "system.listMethods" => ListMethods().Cast<object?>().ToArray(),
            "system.multicall" => HandleMulticall(request).Cast<object?>().ToArray(),
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(request.MethodName) || (result is string s && s.Length == 0 && !KnownEmptyResponseMethods.Contains(request.MethodName)))
        {
            FlrigTraceLog.Write($"UNKNOWN_OR_EMPTY method={request.MethodName}");
        }

        return result;
    }

    private static readonly HashSet<string> KnownEmptyResponseMethods = new(StringComparer.Ordinal)
    {
        "rig.set_AB",
    };

    private int GetVfo()
    {
        lock (_gate)
        {
            return (int)_state.FrequencyHz;
        }
    }

    private string GetInfo()
    {
        lock (_gate)
        {
            return $"{FakeRadio} {_state.FrequencyHz / 1_000_000.0:0.000} MHz {_state.Mode.ToString().ToUpperInvariant()}";
        }
    }

    private int SetVfo(XmlRpcRequest request)
    {
        var hzText = request.Parameters.FirstOrDefault()?.AsString() ?? "0";
        var hz = long.TryParse(hzText, out var parsed)
            ? parsed
            : (long)(double.TryParse(hzText, out var parsedDouble) ? parsedDouble : 0d);

        var handler = _setFrequencyAsync;
        if (handler is not null)
        {
            handler(hz, CancellationToken.None).GetAwaiter().GetResult();
        }

        lock (_gate)
        {
            _state = _state with { FrequencyHz = hz };
        }
        _events.OnNext(new InteropEvent("flrig", $"set_vfo {hz}"));
        return (int)hz;
    }

    private string SetAb(XmlRpcRequest request)
    {
        var vfo = request.Parameters.FirstOrDefault()?.AsString() ?? "A";
        _events.OnNext(new InteropEvent("flrig", $"set_ab {vfo}"));
        return vfo;
    }

    private string GetMode()
    {
        lock (_gate)
        {
            return FormatMode(_state.Mode);
        }
    }

    private string[] GetModes() =>
    [
        "LSB", "USB", "AM", "FM", "CW", "CW-R",
        "RTTY", "RTTY-R", "LSB-D", "USB-D", "AM-D", "FM-D",
    ];

    private string SetMode(XmlRpcRequest request)
    {
        var modeText = request.Parameters.FirstOrDefault()?.AsString() ?? "USB";
        var mode = ParseFlrigMode(modeText);

        var handler = _setModeAsync;
        if (handler is not null)
        {
            handler(mode, CancellationToken.None).GetAwaiter().GetResult();
        }

        lock (_gate)
        {
            _state = _state with { Mode = mode };
        }

        _events.OnNext(new InteropEvent("flrig", $"set_mode {modeText}"));
        return modeText;
    }

    private int GetPtt()
    {
        lock (_gate)
        {
            return _state.IsPttActive ? 1 : 0;
        }
    }

    private int SetPtt(XmlRpcRequest request)
    {
        var enabled = (request.Parameters.FirstOrDefault()?.AsInt32() ?? 0) != 0;
        var handler = _setPttAsync;
        if (handler is not null)
        {
            handler(enabled, CancellationToken.None).GetAwaiter().GetResult();
        }

        lock (_gate)
        {
            _state = _state with { IsPttActive = enabled };
        }
        _events.OnNext(new InteropEvent("flrig", $"set_ptt {(enabled ? 1 : 0)}"));
        return enabled ? 1 : 0;
    }

    private string GetSmeter()
    {
        lock (_gate)
        {
            return Math.Min(100, _state.Smeter * 11).ToString();
        }
    }

    private string GetBandwidth()
    {
        lock (_gate)
        {
            return _state.FilterWidthHz.ToString();
        }
    }

    private string[] GetBandwidths() => ["200", "500", "1000", "1800", "2400", "3000"];

    private string GetSideband()
    {
        lock (_gate)
        {
            var mode = FormatMode(_state.Mode);
            return mode.Contains("USB", StringComparison.OrdinalIgnoreCase) ? "U" : "L";
        }
    }

    private string GetTrxState()
    {
        lock (_gate)
        {
            return _state.IsPttActive ? "TX" : "RX";
        }
    }

    private static string[] ListMethods() =>
    [
        "main.get_version",
        "main.get_frequency",
        "main.set_frequency",
        "main.get_trx_state",
        "rig.get_xcvr",
        "rig.get_info",
        "rig.get_vfo",
        "rig.get_AB",
        "rig.set_vfo",
        "rig.set_AB",
        "rig.get_mode",
        "rig.get_modeA",
        "rig.get_modes",
        "rig.set_mode",
        "rig.get_ptt",
        "rig.set_ptt",
        "rig.get_smeter",
        "rig.get_power",
        "rig.get_pwrmeter",
        "rig.get_pwrmax",
        "rig.get_swrmeter",
        "rig.get_bw",
        "rig.get_bws",
        "rig.get_split",
        "rig.get_notch",
        "rig.get_sideband",
        "rig.get_pwrlevel",
        "rig.set_pwrlevel",
        "system.listMethods",
        "system.multicall",
    ];

    private object?[] HandleMulticall(XmlRpcRequest request)
    {
        var xml = request.Parameters.FirstOrDefault()?.RawValue ?? string.Empty;
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        var parsedCalls = MulticallParser.Parse(xml);
        return parsedCalls
            .Select(call =>
            {
                try
                {
                    var value = Dispatch(call);
                    return (object?)new object?[] { value };
                }
                catch (FlrigFaultException ex)
                {
                    return new object?[] { $"fault:{ex.Fault.Code}:{ex.Fault.Message}" };
                }
            })
            .ToArray();
    }

    private static string FormatMode(RadioMode mode) => mode switch
    {
        RadioMode.Cw => "CW",
        RadioMode.Rtty => "RTTY",
        RadioMode.Lsb => "LSB",
        RadioMode.LsbData => "LSB-D",
        RadioMode.Usb => "USB",
        RadioMode.UsbData => "USB-D",
        RadioMode.Am => "AM",
        RadioMode.Fm => "FM",
        _ => mode.ToString().ToUpperInvariant(),
    };

    private static RadioMode ParseFlrigMode(string modeText)
    {
        var normalized = (modeText ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "LSB" => RadioMode.Lsb,
            "LSB-D" => RadioMode.LsbData,
            "USB" => RadioMode.Usb,
            "USB-D" => RadioMode.UsbData,
            "AM" => RadioMode.Am,
            "AM-D" => RadioMode.Am,
            "FM" => RadioMode.Fm,
            "FM-D" => RadioMode.Fm,
            "CW" => RadioMode.Cw,
            "CW-R" => RadioMode.Cw,
            "CWR" => RadioMode.Cw,
            "RTTY" => RadioMode.Rtty,
            "RTTY-R" => RadioMode.Rtty,
            "RTTYR" => RadioMode.Rtty,
            _ when Enum.TryParse<RadioMode>(normalized, true, out var parsed) => parsed,
            _ => RadioMode.Usb,
        };
    }
}
