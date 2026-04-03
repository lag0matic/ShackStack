using ShackStack.Core.Abstractions.Models;
using ShackStack.Infrastructure.Radio.Civ;
using System.Text;

namespace ShackStack.Infrastructure.Radio.Icom;

internal sealed class IcomCivCommands(CivSession session)
{
    private const int CompressorRawMin = 11;
    private const int CompressorRawMax = 241;
    private static readonly int[,] CwSpeedLookup =
    {
        { 0, 6 }, { 7, 7 }, { 12, 8 }, { 19, 9 }, { 25, 10 }, { 31, 11 }, { 37, 12 }, { 43, 13 }, { 49, 14 },
        { 55, 15 }, { 61, 16 }, { 67, 17 }, { 73, 18 }, { 79, 19 }, { 84, 20 }, { 91, 21 }, { 97, 22 }, { 103, 23 },
        { 108, 24 }, { 114, 25 }, { 121, 26 }, { 128, 27 }, { 134, 28 }, { 140, 29 }, { 144, 30 }, { 151, 31 },
        { 156, 32 }, { 164, 33 }, { 169, 34 }, { 175, 35 }, { 182, 36 }, { 188, 37 }, { 192, 38 }, { 199, 39 },
        { 203, 40 }, { 211, 41 }, { 215, 42 }, { 224, 43 }, { 229, 44 }, { 234, 45 }, { 239, 46 }, { 244, 47 },
        { 250, 48 },
    };

    private static readonly HashSet<char> AllowedCwCharacters =
    [
        '0','1','2','3','4','5','6','7','8','9',
        'A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z',
        'a','b','c','d','e','f','g','h','i','j','k','l','m','n','o','p','q','r','s','t','u','v','w','x','y','z',
        '/','?','.','-',',',':','\'','(',')','=','+','"','@','^',' '
    ];

    public async Task<long> GetFrequencyAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.GetFrequency,
            payload: [],
            matcher: response => response.Command == IcomCivConstants.GetFrequency && response.Source == radioAddress,
            cancellationToken).ConfigureAwait(false);

        return frame is null ? 0L : IcomFrequencyCodec.Decode(frame.Payload);
    }

    public Task SetFrequencyAsync(byte radioAddress, long hz, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.SetFrequency,
            payload: IcomFrequencyCodec.Encode(hz),
            matcher: null,
            cancellationToken);

    public Task SelectVfoAAsync(byte radioAddress, CancellationToken cancellationToken) =>
        SelectVfoAsync(radioAddress, IcomCivConstants.VfoASubcommand, cancellationToken);

    public Task SelectVfoBAsync(byte radioAddress, CancellationToken cancellationToken) =>
        SelectVfoAsync(radioAddress, IcomCivConstants.VfoBSubcommand, cancellationToken);

    public Task EqualizeVfosAsync(byte radioAddress, CancellationToken cancellationToken) =>
        SelectVfoAsync(radioAddress, IcomCivConstants.VfoEqualizeSubcommand, cancellationToken);

    public Task ExchangeVfosAsync(byte radioAddress, CancellationToken cancellationToken) =>
        SelectVfoAsync(radioAddress, IcomCivConstants.VfoExchangeSubcommand, cancellationToken);

    public async Task<bool> GetSplitAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.SplitCommand,
            payload: [],
            matcher: response => response.Command == IcomCivConstants.SplitCommand && response.Source == radioAddress,
            cancellationToken).ConfigureAwait(false);

        return frame is not null && frame.Payload.Length > 0 && frame.Payload[0] != 0x00;
    }

    public Task SetSplitAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.SplitCommand,
            payload: [(byte)(enabled ? 0x01 : 0x00)],
            matcher: response => response.Kind == CivFrameKind.Acknowledge || response.Kind == CivFrameKind.NegativeAcknowledge,
            cancellationToken);

    public async Task<(RadioMode Mode, int FilterWidthHz, int FilterSlot)> GetModeAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var vfoModeFrame = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.VfoMode,
            payload: [0x00],
            matcher: response => response.Command == IcomCivConstants.VfoMode
                && response.Source == radioAddress
                && response.Payload.Length >= 4,
            cancellationToken).ConfigureAwait(false);

        if (vfoModeFrame is not null && vfoModeFrame.Payload.Length >= 4)
        {
            var mode = IcomModeCodec.Decode(vfoModeFrame.Payload[1], vfoModeFrame.Payload[2]);
            var filterSlot = vfoModeFrame.Payload[3];
            return (mode, IcomModeCodec.DecodeFilterWidth(mode, filterSlot), filterSlot);
        }

        var frame = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.GetMode,
            payload: [],
            matcher: response => response.Command == IcomCivConstants.GetMode && response.Source == radioAddress,
            cancellationToken).ConfigureAwait(false);

        if (frame is null || frame.Payload.Length == 0)
        {
            return (RadioMode.Usb, 2400, 2);
        }

        var fallbackMode = IcomModeCodec.Decode(frame.Payload[0]);
        var fallbackFilterSlot = frame.Payload.Length > 1 ? frame.Payload[1] : (byte)0x02;
        return (fallbackMode, IcomModeCodec.DecodeFilterWidth(fallbackMode, fallbackFilterSlot), fallbackFilterSlot);
    }

    public Task SetModeAsync(byte radioAddress, RadioMode mode, byte filterSlot, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.VfoMode,
            payload: [0x00, IcomModeCodec.Encode(mode), IcomModeCodec.EncodeDataMode(mode), filterSlot],
            matcher: null,
            cancellationToken);

    public async Task<bool> GetPttAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.SetControlState,
            payload: [IcomCivConstants.PttSubcommand],
            matcher: response => response.Command == IcomCivConstants.SetControlState
                && response.Source == radioAddress
                && response.Payload.Length >= 2
                && response.Payload[0] == IcomCivConstants.PttSubcommand,
            cancellationToken).ConfigureAwait(false);

        return frame is not null && frame.Payload.Length >= 2 && frame.Payload[1] != 0x00;
    }

    public async Task<int> GetSmeterAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ReadMeter,
            payload: [IcomCivConstants.SmeterSubcommand],
            matcher: response => response.Command == IcomCivConstants.ReadMeter && response.Source == radioAddress,
            cancellationToken).ConfigureAwait(false);

        if (frame is null || frame.Payload.Length < 2)
        {
            return 0;
        }

        var raw = frame.Payload.Length >= 3
            ? (frame.Payload[^2] << 8) | frame.Payload[^1]
            : frame.Payload[^1];
        return Math.Clamp((int)Math.Round(raw / 28.0), 0, 10);
    }

    public async Task<int> GetCwPitchAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetControlLevelAsync(radioAddress, IcomCivConstants.CwPitchLevelSubcommand, cancellationToken).ConfigureAwait(false);
        if (frame is null || frame.Payload.Length < 3)
        {
            return 700;
        }

        var knob = DecodeBcdBe(frame.Payload[1..3]);
        return Math.Clamp((int)Math.Round(300.0 + (knob * 600.0 / 255.0)), 300, 900);
    }

    public async Task<int> GetMicGainPercentAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetControlLevelAsync(radioAddress, IcomCivConstants.MicGainLevelSubcommand, cancellationToken).ConfigureAwait(false);
        if (frame is null || frame.Payload.Length < 3)
        {
            VoiceTraceLog.Write("MIC get -> no response");
            return 50;
        }

        var rigValue = DecodeBcdBe(frame.Payload[1..3]);
        var percent = Math.Clamp((int)Math.Floor(rigValue * 100.0 / 255.0), 0, 100);
        VoiceTraceLog.Write($"MIC get raw={rigValue} percent={percent} payload={FormatBytes(frame.Payload)}");
        return percent;
    }

    public async Task SetMicGainPercentAsync(byte radioAddress, int percent, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var rigValue = Math.Clamp((int)Math.Round(clamped * 255.0 / 100.0), 0, 255);
        VoiceTraceLog.Write($"MIC set requested percent={percent} clamped={clamped} raw={rigValue}");
        var response = await SetControlLevelAsync(radioAddress, IcomCivConstants.MicGainLevelSubcommand, rigValue, cancellationToken).ConfigureAwait(false);
        VoiceTraceLog.Write($"MIC set response kind={(response?.Kind.ToString() ?? "null")} cmd=0x{response?.Command:X2}");
    }

    public async Task<int> GetCompressionLevelAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetControlLevelAsync(radioAddress, IcomCivConstants.CompressorLevelSubcommand, cancellationToken).ConfigureAwait(false);
        if (frame is null || frame.Payload.Length < 3)
        {
            VoiceTraceLog.Write("COMP level get -> no response");
            return 0;
        }

        var rigValue = DecodeBcdBe(frame.Payload[1..3]);
        var level = DecodeCompressionLevel(rigValue);
        VoiceTraceLog.Write($"COMP level get raw={rigValue} level={level} payload={FormatBytes(frame.Payload)}");
        return level;
    }

    public async Task SetCompressionLevelAsync(byte radioAddress, int level, CancellationToken cancellationToken)
    {
        var rigValue = EncodeCompressionLevel(level);
        VoiceTraceLog.Write($"COMP level set requested level={level} raw={rigValue}");
        var response = await SetControlLevelAsync(radioAddress, IcomCivConstants.CompressorLevelSubcommand, rigValue, cancellationToken).ConfigureAwait(false);
        VoiceTraceLog.Write($"COMP level set response kind={(response?.Kind.ToString() ?? "null")} cmd=0x{response?.Command:X2}");
    }

    public async Task<bool> GetCompressionEnabledAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetFunctionStateAsync(radioAddress, IcomCivConstants.CompressorFunctionSubcommand, cancellationToken).ConfigureAwait(false);
        var enabled = frame is not null && frame.Payload.Length >= 2 && frame.Payload[1] != 0x00;
        VoiceTraceLog.Write(frame is null
            ? "COMP func get -> no response"
            : $"COMP func get enabled={enabled} payload={FormatBytes(frame.Payload)}");
        return enabled;
    }

    public Task SetCompressionEnabledAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        SetCompressionEnabledLoggedAsync(radioAddress, enabled, cancellationToken);

    public async Task<int> GetRfPowerPercentAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetControlLevelAsync(radioAddress, IcomCivConstants.RfPowerLevelSubcommand, cancellationToken).ConfigureAwait(false);
        if (frame is null || frame.Payload.Length < 3)
        {
            VoiceTraceLog.Write("RF power get -> no response");
            return 100;
        }

        var rigValue = DecodeBcdBe(frame.Payload[1..3]);
        var percent = Math.Clamp((int)Math.Floor(rigValue * 100.0 / 255.0), 0, 100);
        VoiceTraceLog.Write($"RF power get raw={rigValue} percent={percent} payload={FormatBytes(frame.Payload)}");
        return percent;
    }

    public async Task SetRfPowerPercentAsync(byte radioAddress, int percent, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var rigValue = Math.Clamp((int)Math.Round(clamped * 255.0 / 100.0), 0, 255);
        VoiceTraceLog.Write($"RF power set requested percent={percent} clamped={clamped} raw={rigValue}");
        var response = await SetControlLevelAsync(radioAddress, IcomCivConstants.RfPowerLevelSubcommand, rigValue, cancellationToken).ConfigureAwait(false);
        VoiceTraceLog.Write($"RF power set response kind={(response?.Kind.ToString() ?? "null")} cmd=0x{response?.Command:X2}");
    }

    public async Task<int> GetNoiseReductionLevelAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetControlLevelAsync(radioAddress, IcomCivConstants.NoiseReductionLevelSubcommand, cancellationToken).ConfigureAwait(false);
        if (frame is null || frame.Payload.Length < 3)
        {
            return 0;
        }

        var rigValue = DecodeBcdBe(frame.Payload[1..3]);
        return Math.Clamp((int)Math.Round(rigValue * 15.0 / 255.0), 0, 15);
    }

    public Task SetNoiseReductionLevelAsync(byte radioAddress, int level, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(level, 0, 15);
        var rigValue = Math.Clamp((int)Math.Round(clamped * 255.0 / 15.0), 0, 255);
        return SetControlLevelAsync(radioAddress, IcomCivConstants.NoiseReductionLevelSubcommand, rigValue, cancellationToken);
    }

    public async Task<bool> GetNoiseBlankerEnabledAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetFunctionStateAsync(radioAddress, IcomCivConstants.NoiseBlankerFunctionSubcommand, cancellationToken).ConfigureAwait(false);
        return frame is not null && frame.Payload.Length >= 2 && frame.Payload[1] != 0x00;
    }

    public Task SetNoiseBlankerEnabledAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        SetFunctionStateAsync(radioAddress, IcomCivConstants.NoiseBlankerFunctionSubcommand, enabled, cancellationToken);

    public async Task<bool> GetNoiseReductionEnabledAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetFunctionStateAsync(radioAddress, IcomCivConstants.NoiseReductionFunctionSubcommand, cancellationToken).ConfigureAwait(false);
        return frame is not null && frame.Payload.Length >= 2 && frame.Payload[1] != 0x00;
    }

    public Task SetNoiseReductionEnabledAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        SetFunctionStateAsync(radioAddress, IcomCivConstants.NoiseReductionFunctionSubcommand, enabled, cancellationToken);

    public async Task<bool> GetIpPlusEnabledAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetFunctionStateAsync(radioAddress, IcomCivConstants.IpPlusFunctionSubcommand, cancellationToken).ConfigureAwait(false);
        return frame is not null && frame.Payload.Length >= 2 && frame.Payload[1] != 0x00;
    }

    public Task SetIpPlusEnabledAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        SetFunctionStateAsync(radioAddress, IcomCivConstants.IpPlusFunctionSubcommand, enabled, cancellationToken);

    public async Task<bool> GetFilterShapeSoftAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetFunctionStateAsync(radioAddress, IcomCivConstants.FilterShapeFunctionSubcommand, cancellationToken).ConfigureAwait(false);
        return frame is not null && frame.Payload.Length >= 2 && frame.Payload[1] != 0x00;
    }

    public Task SetFilterShapeSoftAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        SetFunctionStateAsync(radioAddress, IcomCivConstants.FilterShapeFunctionSubcommand, enabled, cancellationToken);

    public async Task<bool> GetAutoNotchEnabledAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetFunctionStateAsync(radioAddress, IcomCivConstants.AutoNotchFunctionSubcommand, cancellationToken).ConfigureAwait(false);
        return frame is not null && frame.Payload.Length >= 2 && frame.Payload[1] != 0x00;
    }

    public Task SetAutoNotchEnabledAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        SetFunctionStateAsync(radioAddress, IcomCivConstants.AutoNotchFunctionSubcommand, enabled, cancellationToken);

    public async Task<bool> GetManualNotchEnabledAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetFunctionStateAsync(radioAddress, IcomCivConstants.ManualNotchFunctionSubcommand, cancellationToken).ConfigureAwait(false);
        return frame is not null && frame.Payload.Length >= 2 && frame.Payload[1] != 0x00;
    }

    public Task SetManualNotchEnabledAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        SetFunctionStateAsync(radioAddress, IcomCivConstants.ManualNotchFunctionSubcommand, enabled, cancellationToken);

    public async Task<int> GetManualNotchWidthAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetFunctionStateAsync(radioAddress, IcomCivConstants.ManualNotchWidthFunctionSubcommand, cancellationToken).ConfigureAwait(false);
        if (frame is null || frame.Payload.Length < 2)
        {
            return 1;
        }

        return Math.Clamp((int)frame.Payload[1], 0, 2);
    }

    public Task SetManualNotchWidthAsync(byte radioAddress, int width, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ControlFunctionCommand,
            payload: [IcomCivConstants.ManualNotchWidthFunctionSubcommand, (byte)Math.Clamp(width, 0, 2)],
            matcher: response => response.Kind == CivFrameKind.Acknowledge || response.Kind == CivFrameKind.NegativeAcknowledge,
            cancellationToken);

    public async Task<int> GetManualNotchPositionAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetControlLevelAsync(radioAddress, IcomCivConstants.ManualNotchPositionLevelSubcommand, cancellationToken).ConfigureAwait(false);
        if (frame is null || frame.Payload.Length < 3)
        {
            return 128;
        }

        return Math.Clamp(DecodeBcdBe(frame.Payload[1..3]), 0, 255);
    }

    public Task SetManualNotchPositionAsync(byte radioAddress, int position, CancellationToken cancellationToken) =>
        SetControlLevelAsync(radioAddress, IcomCivConstants.ManualNotchPositionLevelSubcommand, Math.Clamp(position, 0, 255), cancellationToken);

    public async Task SetCwPitchAsync(byte radioAddress, int pitchHz, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(pitchHz, 300, 900);
        var knob = Math.Clamp((int)Math.Round((clamped - 300) * (255.0 / 600.0)), 0, 255);
        await SetControlLevelAsync(radioAddress, IcomCivConstants.CwPitchLevelSubcommand, knob, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetCwKeySpeedAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await GetControlLevelAsync(radioAddress, IcomCivConstants.KeySpeedLevelSubcommand, cancellationToken).ConfigureAwait(false);
        if (frame is null || frame.Payload.Length < 3)
        {
            return 20;
        }

        var rigValue = DecodeBcdBe(frame.Payload[1..3]);
        for (var i = 0; i < CwSpeedLookup.GetLength(0); i++)
        {
            if (CwSpeedLookup[i, 0] >= rigValue)
            {
                return CwSpeedLookup[i, 1];
            }
        }

        return 48;
    }

    public async Task SetCwKeySpeedAsync(byte radioAddress, int wpm, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(wpm, 6, 48);
        var rigValue = CwSpeedLookup[CwSpeedLookup.GetLength(0) - 1, 0];
        for (var i = 0; i < CwSpeedLookup.GetLength(0); i++)
        {
            if (CwSpeedLookup[i, 1] == clamped)
            {
                rigValue = CwSpeedLookup[i, 0];
                break;
            }
        }

        await SetControlLevelAsync(radioAddress, IcomCivConstants.KeySpeedLevelSubcommand, rigValue, cancellationToken).ConfigureAwait(false);
    }

    public Task SetPttAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.SetControlState,
            payload: [IcomCivConstants.PttSubcommand, (byte)(enabled ? 0x01 : 0x00)],
            matcher: null,
            cancellationToken);

    public Task<CivFrame?> SendCwTextChunkAsync(byte radioAddress, string text, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCwText(text);
        if (normalized.Length == 0)
        {
            return Task.FromResult<CivFrame?>(null);
        }

        return session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.CwMessageContents,
            payload: Encoding.ASCII.GetBytes(normalized),
            matcher: response => response.Command == 0xFB || response.Command == 0xFA,
            cancellationToken);
    }

    public Task<CivFrame?> StopCwSendAsync(byte radioAddress, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.CwMessageContents,
            payload: [0xFF],
            matcher: response => response.Command == 0xFB || response.Command == 0xFA,
            cancellationToken);

    public async Task<int> GetPreampAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: 0x16,
            payload: [0x02],
            matcher: response => response.Command == 0x16 && response.Source == radioAddress,
            cancellationToken).ConfigureAwait(false);

        if (frame is null || frame.Payload.Length < 2 || frame.Payload[0] != 0x02)
        {
            return 0;
        }

        return Math.Clamp((int)frame.Payload[1], 0, 2);
    }

    public Task SetPreampAsync(byte radioAddress, int level, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: 0x16,
            payload: [0x02, (byte)Math.Clamp(level, 0, 2)],
            matcher: null,
            cancellationToken);

    public async Task<int> GetAttenuatorDbAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: 0x11,
            payload: [],
            matcher: response => response.Command == 0x11 && response.Source == radioAddress,
            cancellationToken).ConfigureAwait(false);

        if (frame is null || frame.Payload.Length < 1)
        {
            return 0;
        }

        var packed = frame.Payload[0];
        return ((packed >> 4) * 10) + (packed & 0x0F);
    }

    public Task SetAttenuatorAsync(byte radioAddress, bool enabled, int db, CancellationToken cancellationToken)
    {
        var value = Math.Clamp(enabled ? db : 0, 0, 99);
        var packed = (byte)(((value / 10) << 4) | (value % 10));
        return session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: 0x11,
            payload: [packed],
            matcher: null,
            cancellationToken);
    }

    public async Task<bool> GetTunerEnabledAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var frame = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.SetControlState,
            payload: [0x01],
            matcher: response => response.Command == IcomCivConstants.SetControlState
                && response.Source == radioAddress
                && response.Payload.Length >= 2
                && response.Payload[0] == 0x01,
            cancellationToken).ConfigureAwait(false);

        return frame is not null && frame.Payload.Length >= 2 && frame.Payload[1] != 0x00;
    }

    public Task SetTunerEnabledAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.SetControlState,
            payload: [0x01, (byte)(enabled ? 0x01 : 0x00)],
            matcher: null,
            cancellationToken);

    public Task RetuneAsync(byte radioAddress, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.SetControlState,
            payload: [0x01, 0x02],
            matcher: null,
            cancellationToken);

    public async Task<bool> EnableScopeOutputAsync(byte radioAddress, CancellationToken cancellationToken)
    {
        var scopeEnabled = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ScopeCommand,
            payload: [IcomCivConstants.ScopeEnableSubcommand, 0x01],
            matcher: response => response.Kind == CivFrameKind.Acknowledge || response.Kind == CivFrameKind.NegativeAcknowledge,
            cancellationToken).ConfigureAwait(false);

        var outputEnabled = await session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ScopeCommand,
            payload: [IcomCivConstants.ScopeOutputEnableSubcommand, 0x01],
            matcher: response => response.Kind == CivFrameKind.Acknowledge || response.Kind == CivFrameKind.NegativeAcknowledge,
            cancellationToken).ConfigureAwait(false);

        return scopeEnabled?.Kind != CivFrameKind.NegativeAcknowledge
            && outputEnabled?.Kind != CivFrameKind.NegativeAcknowledge;
    }

    public Task<CivFrame?> GetScopeModeAsync(byte radioAddress, CancellationToken cancellationToken) =>
        QueryScopeStateAsync(radioAddress, IcomCivConstants.ScopeModeSubcommand, cancellationToken);

    public Task<CivFrame?> GetScopeSpanAsync(byte radioAddress, CancellationToken cancellationToken) =>
        QueryScopeStateAsync(radioAddress, IcomCivConstants.ScopeSpanSubcommand, cancellationToken);

    public Task<CivFrame?> GetScopeEdgeAsync(byte radioAddress, CancellationToken cancellationToken) =>
        QueryScopeStateAsync(radioAddress, IcomCivConstants.ScopeEdgeSubcommand, cancellationToken);

    public Task<CivFrame?> GetScopeHoldAsync(byte radioAddress, CancellationToken cancellationToken) =>
        QueryScopeStateAsync(radioAddress, IcomCivConstants.ScopeHoldSubcommand, cancellationToken);

    public Task<CivFrame?> GetScopeRefAsync(byte radioAddress, CancellationToken cancellationToken) =>
        QueryScopeStateAsync(radioAddress, IcomCivConstants.ScopeRefSubcommand, cancellationToken);

    public Task<CivFrame?> GetScopeSpeedAsync(byte radioAddress, CancellationToken cancellationToken) =>
        QueryScopeStateAsync(radioAddress, IcomCivConstants.ScopeSpeedSubcommand, cancellationToken);

    public Task SetScopeHoldAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ScopeCommand,
            payload: [IcomCivConstants.ScopeHoldSubcommand, (byte)(enabled ? 0x01 : 0x00)],
            matcher: response => response.Kind == CivFrameKind.Acknowledge || response.Kind == CivFrameKind.NegativeAcknowledge,
            cancellationToken);

    private Task<CivFrame?> QueryScopeStateAsync(byte radioAddress, byte subcommand, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ScopeCommand,
            payload: [subcommand],
            matcher: response => response.Command == IcomCivConstants.ScopeCommand
                && response.Source == radioAddress
                && response.Payload.Length >= 1
                && response.Payload[0] == subcommand,
            cancellationToken);

    private Task SelectVfoAsync(byte radioAddress, byte subcommand, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.VfoCommand,
            payload: [subcommand],
            matcher: response => response.Kind == CivFrameKind.Acknowledge || response.Kind == CivFrameKind.NegativeAcknowledge,
            cancellationToken);

    private Task<CivFrame?> GetControlLevelAsync(byte radioAddress, byte subcommand, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ControlLevelCommand,
            payload: [subcommand],
            matcher: response => response.Command == IcomCivConstants.ControlLevelCommand
                && response.Source == radioAddress
                && response.Payload.Length >= 1
                && response.Payload[0] == subcommand,
            cancellationToken);

    private Task<CivFrame?> SetControlLevelAsync(byte radioAddress, byte subcommand, int value, CancellationToken cancellationToken)
    {
        var encoded = EncodeBcdBe(value);
        return session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ControlLevelCommand,
            payload: [subcommand, encoded[0], encoded[1]],
            matcher: response => response.Kind == CivFrameKind.Acknowledge || response.Kind == CivFrameKind.NegativeAcknowledge,
            cancellationToken);
    }

    private Task<CivFrame?> GetFunctionStateAsync(byte radioAddress, byte subcommand, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ControlFunctionCommand,
            payload: [subcommand],
            matcher: response => response.Command == IcomCivConstants.ControlFunctionCommand
                && response.Source == radioAddress
                && response.Payload.Length >= 1
                && response.Payload[0] == subcommand,
            cancellationToken);

    private Task<CivFrame?> SetFunctionStateAsync(byte radioAddress, byte subcommand, bool enabled, CancellationToken cancellationToken) =>
        session.SendCommandAsync(
            destination: radioAddress,
            source: IcomCivConstants.ControllerAddress,
            command: IcomCivConstants.ControlFunctionCommand,
            payload: [subcommand, (byte)(enabled ? 0x01 : 0x00)],
            matcher: response => response.Kind == CivFrameKind.Acknowledge || response.Kind == CivFrameKind.NegativeAcknowledge,
            cancellationToken);

    private static byte[] EncodeBcdBe(int value)
    {
        value = Math.Clamp(value, 0, 9999);
        var thousands = (value / 1000) % 10;
        var hundreds = (value / 100) % 10;
        var tens = (value / 10) % 10;
        var ones = value % 10;
        return [(byte)((thousands << 4) | hundreds), (byte)((tens << 4) | ones)];
    }

    private static int DecodeBcdBe(ReadOnlySpan<byte> bytes)
    {
        var value = 0;
        foreach (var b in bytes)
        {
            value = (value * 10) + ((b >> 4) & 0x0F);
            value = (value * 10) + (b & 0x0F);
        }

        return value;
    }

    private static int EncodeCompressionLevel(int level)
    {
        var clamped = Math.Clamp(level, 0, 10);
        if (clamped == 0)
        {
            return CompressorRawMin;
        }

        var raw = CompressorRawMin + (int)Math.Round(clamped * (CompressorRawMax - CompressorRawMin) / 10.0);
        return Math.Clamp(raw, CompressorRawMin, CompressorRawMax);
    }

    private static int DecodeCompressionLevel(int raw)
    {
        if (raw <= CompressorRawMin)
        {
            return 0;
        }

        var normalized = (raw - CompressorRawMin) * 10.0 / (CompressorRawMax - CompressorRawMin);
        return Math.Clamp((int)Math.Round(normalized), 0, 10);
    }

    private async Task<CivFrame?> SetCompressionEnabledLoggedAsync(byte radioAddress, bool enabled, CancellationToken cancellationToken)
    {
        VoiceTraceLog.Write($"COMP func set enabled={enabled}");
        var response = await SetFunctionStateAsync(radioAddress, IcomCivConstants.CompressorFunctionSubcommand, enabled, cancellationToken).ConfigureAwait(false);
        VoiceTraceLog.Write($"COMP func set response kind={(response?.Kind.ToString() ?? "null")} cmd=0x{response?.Command:X2}");
        return response;
    }

    private static string FormatBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return "<empty>";
        }

        return string.Join(" ", bytes.ToArray().Select(static b => $"0x{b:X2}"));
    }

    public static string NormalizeCwText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '))
        {
            sb.Append(AllowedCwCharacters.Contains(ch) ? ch : '?');
        }

        return sb.ToString().Trim();
    }
}
