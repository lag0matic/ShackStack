using System.Diagnostics;
using System.Globalization;
using System.Text;
using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Infrastructure.Decoders;

internal sealed class WsjtxExternalWaveformPort
{
    private const string MsysUcrtBin = @"C:\msys64\ucrt64\bin";
    private readonly string? _ft4SimPath;
    private readonly string? _ft8SimPath;
    private readonly string? _jt49SimPath;
    private readonly string? _jt65SimPath;
    private readonly string? _jt4SimPath;
    private readonly string? _msk144SimPath;
    private readonly string? _q65SimPath;
    private readonly string? _fst4SimPath;
    private readonly string? _wsprSimPath;
    private readonly string? _js8TonePath;
    private readonly string _stagingRoot;

    private WsjtxExternalWaveformPort(
        string? ft4SimPath,
        string? ft8SimPath,
        string? jt49SimPath,
        string? jt65SimPath,
        string? jt4SimPath,
        string? msk144SimPath,
        string? q65SimPath,
        string? fst4SimPath,
        string? wsprSimPath,
        string? js8TonePath,
        string stagingRoot)
    {
        _ft4SimPath = ft4SimPath;
        _ft8SimPath = ft8SimPath;
        _jt49SimPath = jt49SimPath;
        _jt65SimPath = jt65SimPath;
        _jt4SimPath = jt4SimPath;
        _msk144SimPath = msk144SimPath;
        _q65SimPath = q65SimPath;
        _fst4SimPath = fst4SimPath;
        _wsprSimPath = wsprSimPath;
        _js8TonePath = js8TonePath;
        _stagingRoot = stagingRoot;
        Directory.CreateDirectory(_stagingRoot);
    }

    public static WsjtxExternalWaveformPort CreateDefault()
    {
        var bundledWaveformBinRoot = Path.Combine(AppContext.BaseDirectory, "wsjtx-tools", "waveform", "bin");
        var bundledFallbackBinRoot = Path.Combine(AppContext.BaseDirectory, "wsjtx-tools", "runtime", "bin");
        var waveformBinRoot = Environment.GetEnvironmentVariable("SHACKSTACK_WSJTX_WAVEFORM_BIN")
            ?? (Directory.Exists(bundledWaveformBinRoot)
                ? bundledWaveformBinRoot
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "ShackStack",
                    "wsjtx-tools",
                    "waveform",
                    "bin"));
        var fallbackBinRoot = Environment.GetEnvironmentVariable("SHACKSTACK_WSJTX_BIN")
            ?? (Directory.Exists(bundledFallbackBinRoot)
                ? bundledFallbackBinRoot
                : @"C:\WSJT\wsjtx\bin");
        var stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "ShackStack",
            "tx-staging",
            "wsjtx");
        return new WsjtxExternalWaveformPort(
            ResolveTool(waveformBinRoot, fallbackBinRoot, "ft4sim.exe"),
            ResolveTool(waveformBinRoot, fallbackBinRoot, "ft8sim.exe"),
            ResolveTool(waveformBinRoot, fallbackBinRoot, "jt49sim.exe"),
            ResolveTool(waveformBinRoot, fallbackBinRoot, "jt65sim.exe"),
            ResolveTool(waveformBinRoot, fallbackBinRoot, "jt4sim.exe"),
            ResolveTool(waveformBinRoot, fallbackBinRoot, "msk144sim.exe"),
            ResolveTool(waveformBinRoot, fallbackBinRoot, "q65sim.exe"),
            ResolveTool(waveformBinRoot, fallbackBinRoot, "fst4sim.exe"),
            ResolveTool(waveformBinRoot, fallbackBinRoot, "wsprsim.exe"),
            Environment.GetEnvironmentVariable("SHACKSTACK_JS8_TONES_PATH")
            ?? ResolveTool(
                Path.Combine(AppContext.BaseDirectory, "js8call-tools", "runtime", "bin"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "ShackStack",
                    "js8call-tools",
                    "runtime",
                    "bin"),
                "js8tones.exe"),
            stagingRoot);
    }

    public Task<WsjtxPreparedTransmitResult> PrepareAsync(string modeLabel, string messageText, int txAudioFrequencyHz, double cycleLengthSeconds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return Task.FromResult(new WsjtxPreparedTransmitResult(false, "No TX message is staged", null, null));
        }

        if (messageText.Contains("<GRID>", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new WsjtxPreparedTransmitResult(false, "Replace placeholder fields before preparing TX audio", null, null));
        }

        return Task.Run(() => PrepareInternal(modeLabel, messageText.Trim(), txAudioFrequencyHz, cycleLengthSeconds, ct), ct);
    }

    private WsjtxPreparedTransmitResult PrepareInternal(string modeLabel, string messageText, int txAudioFrequencyHz, double cycleLengthSeconds, CancellationToken ct)
    {
        var mode = modeLabel.Trim().ToUpperInvariant();
        if (mode.StartsWith("JS8 ", StringComparison.Ordinal))
        {
            return PrepareJs8Internal(modeLabel, messageText, txAudioFrequencyHz, cycleLengthSeconds, ct);
        }

        var processPath = ResolveProcessPath(mode);
        if (processPath is null)
        {
            return new WsjtxPreparedTransmitResult(
                false,
                $"{modeLabel} TX waveform generation is not available through the installed WSJT-X tools",
                null,
                null);
        }

        var runDirectory = Path.Combine(_stagingRoot, $"{mode.ToLowerInvariant()}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}");
        Directory.CreateDirectory(runDirectory);

        var arguments = BuildArguments(mode, messageText, txAudioFrequencyHz, cycleLengthSeconds);
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = runDirectory,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        if (Directory.Exists(MsysUcrtBin))
        {
            var existingPath = startInfo.Environment.TryGetValue("PATH", out var pathValue)
                ? pathValue
                : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = string.IsNullOrWhiteSpace(existingPath)
                ? MsysUcrtBin
                : $"{MsysUcrtBin};{existingPath}";
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        ct.ThrowIfCancellationRequested();

        if (!IsSuccessfulExitCode(mode, process.ExitCode))
        {
            return new WsjtxPreparedTransmitResult(
                false,
                $"{Path.GetFileName(processPath)} failed: {FirstLine(stderr) ?? FirstLine(stdout) ?? $"exit {process.ExitCode}"}",
                null,
                null);
        }

        var expectedPattern = GetGeneratedArtifactPattern(mode);
        var generatedWave = Directory.GetFiles(runDirectory, expectedPattern)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (generatedWave is null)
        {
            return new WsjtxPreparedTransmitResult(
                false,
                $"{Path.GetFileName(processPath)} ran but did not produce a staged {expectedPattern} artifact",
                null,
                null);
        }

        var preparedClip = TryLoadPreparedClip(generatedWave);
        string finalPath;
        if (preparedClip is not null)
        {
            finalPath = "In-memory PCM clip";
        }
        else
        {
            var finalName = $"{mode.ToLowerInvariant()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{txAudioFrequencyHz:+0000;-0000;0000}{Path.GetExtension(generatedWave)}"
                .Replace("+", "p", StringComparison.Ordinal)
                .Replace("-", "m", StringComparison.Ordinal);
            finalPath = Path.Combine(_stagingRoot, finalName);
            File.Copy(generatedWave, finalPath, overwrite: true);
        }

        var prepared = new WsjtxPreparedTransmit(
            modeLabel,
            messageText,
            txAudioFrequencyHz,
            Path.GetFileName(processPath),
            finalPath,
            DateTime.UtcNow);

        if (preparedClip is not null)
        {
            TryDeleteDirectory(runDirectory);
        }

        return new WsjtxPreparedTransmitResult(
            true,
            $"{modeLabel} TX signal staged via {prepared.GeneratorName} at {txAudioFrequencyHz:+0;-0;0} Hz",
            prepared,
            preparedClip);
    }

    private string? ResolveProcessPath(string modeLabel) => modeLabel switch
    {
        "FT4" => _ft4SimPath,
        "FT8" => _ft8SimPath,
        "JT4" => _jt49SimPath ?? _jt4SimPath,
        "JT65" => _jt65SimPath,
        "JT9" => _jt49SimPath,
        "MSK144" => _msk144SimPath,
        "Q65" => _q65SimPath,
        "FST4" => _fst4SimPath,
        "FST4W" => _fst4SimPath,
        "WSPR" => _wsprSimPath,
        _ => null,
    };

    private WsjtxPreparedTransmitResult PrepareJs8Internal(string modeLabel, string messageText, int txAudioFrequencyHz, double cycleLengthSeconds, CancellationToken ct)
    {
        if (_js8TonePath is null)
        {
            return new WsjtxPreparedTransmitResult(false, "JS8 TX tone generator is not installed yet", null, null);
        }

        var frames = Js8FrameEncoder.BuildMessageFrames(messageText);
        if (frames.Count == 0)
        {
            return new WsjtxPreparedTransmitResult(false, "JS8 TX text has no encodable characters for this first Varicode pass", null, null);
        }

        var toneFrames = new List<int[]>(frames.Count);
        foreach (var frame in frames)
        {
            var tones = GenerateJs8Tones(frame.Frame, frame.Bits, GetJs8CostasSet(modeLabel), ct);
            if (tones.Length != 79)
            {
                return new WsjtxPreparedTransmitResult(false, $"js8tones.exe returned {tones.Length} tones instead of 79", null, null);
            }

            toneFrames.Add(tones);
        }

        var packedText = string.Concat(frames.Select(frame => frame.Text));
        var clip = SynthesizeJs8Clip(toneFrames, modeLabel, txAudioFrequencyHz, cycleLengthSeconds);
        var prepared = new WsjtxPreparedTransmit(
            modeLabel,
            packedText,
            txAudioFrequencyHz,
            "js8tones.exe",
            "In-memory PCM clip",
            DateTime.UtcNow);

        return new WsjtxPreparedTransmitResult(
            true,
            $"{modeLabel} TX signal staged via JS8Call genjs8 at {txAudioFrequencyHz:+0;-0;0} Hz ({frames.Count} frame{(frames.Count == 1 ? string.Empty : "s")}, {packedText.Length} chars)",
            prepared,
            clip);
    }

    private int[] GenerateJs8Tones(string frame, int frameBits, int costasSet, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _js8TonePath!,
            WorkingDirectory = Path.GetDirectoryName(_js8TonePath!) ?? AppContext.BaseDirectory,
            Arguments = $"{Quote(frame)} {frameBits.ToString(CultureInfo.InvariantCulture)} {costasSet.ToString(CultureInfo.InvariantCulture)}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (Directory.Exists(MsysUcrtBin))
        {
            var existingPath = startInfo.Environment.TryGetValue("PATH", out var pathValue)
                ? pathValue
                : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = string.IsNullOrWhiteSpace(existingPath)
                ? MsysUcrtBin
                : $"{MsysUcrtBin};{existingPath}";
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"js8tones.exe failed: {FirstLine(stderr) ?? FirstLine(stdout) ?? $"exit {process.ExitCode}"}");
        }

        return stdout
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tone) ? tone : -1)
            .Where(tone => tone >= 0)
            .ToArray();
    }

    private static Pcm16AudioClip SynthesizeJs8Clip(IReadOnlyList<int> tones, string modeLabel, int txAudioFrequencyHz, double cycleLengthSeconds)
    {
        return SynthesizeJs8Clip([tones.ToArray()], modeLabel, txAudioFrequencyHz, cycleLengthSeconds);
    }

    private static Pcm16AudioClip SynthesizeJs8Clip(IReadOnlyList<int[]> toneFrames, string modeLabel, int txAudioFrequencyHz, double cycleLengthSeconds)
    {
        const int sampleRate = 12_000;
        const double amplitude = 0.45;
        var nsps = GetJs8SamplesPerSymbol(modeLabel);
        var startDelaySamples = (int)Math.Round(GetJs8StartDelaySeconds(modeLabel) * sampleRate, MidpointRounding.AwayFromZero);
        var samplesPerCycle = Math.Max(
            (int)Math.Round(cycleLengthSeconds * sampleRate, MidpointRounding.AwayFromZero),
            startDelaySamples + (79 * nsps) + sampleRate / 4);
        var totalSamples = Math.Max(samplesPerCycle, samplesPerCycle * Math.Max(1, toneFrames.Count));
        var toneSpacingHz = sampleRate / (double)nsps;
        var pcm = new byte[totalSamples * 2];
        var phase = 0.0;
        var twoPi = Math.PI * 2.0;

        for (var i = 0; i < totalSamples; i++)
        {
            var frameIndex = Math.Min(i / samplesPerCycle, toneFrames.Count - 1);
            var cycleOffset = i - (frameIndex * samplesPerCycle);
            var tones = toneFrames[frameIndex];
            var symbol = (cycleOffset - startDelaySamples) / nsps;
            double sample = 0;
            if (frameIndex >= 0 && frameIndex < toneFrames.Count && symbol >= 0 && symbol < tones.Length)
            {
                var frequency = txAudioFrequencyHz + (tones[symbol] * toneSpacingHz);
                phase += twoPi * frequency / sampleRate;
                if (phase > twoPi)
                {
                    phase -= twoPi;
                }

                sample = Math.Sin(phase) * amplitude;
            }

            var value = (short)Math.Round(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue);
            var offset = i * 2;
            pcm[offset] = (byte)(value & 0xFF);
            pcm[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        return new Pcm16AudioClip(TrimTrailingSilence(pcm, sampleRate, 1), sampleRate, 1);
    }

    private static int GetJs8CostasSet(string modeLabel) =>
        modeLabel.Contains("Normal", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

    private static int GetJs8SamplesPerSymbol(string modeLabel)
    {
        if (modeLabel.Contains("Fast", StringComparison.OrdinalIgnoreCase)) return 1200;
        if (modeLabel.Contains("Turbo", StringComparison.OrdinalIgnoreCase)) return 600;
        if (modeLabel.Contains("Slow", StringComparison.OrdinalIgnoreCase)) return 3840;
        return 1920;
    }

    private static double GetJs8StartDelaySeconds(string modeLabel)
    {
        if (modeLabel.Contains("Fast", StringComparison.OrdinalIgnoreCase)) return 0.2;
        if (modeLabel.Contains("Turbo", StringComparison.OrdinalIgnoreCase)) return 0.1;
        return 0.5;
    }

    private static string BuildArguments(string modeLabel, string messageText, int txAudioFrequencyHz, double cycleLengthSeconds)
    {
        var txHz = txAudioFrequencyHz.ToString(CultureInfo.InvariantCulture);
        return modeLabel switch
        {
            "FT4" => $"{Quote(messageText)} {txHz} 0 0 0 1 99",
            "FT8" => $"{Quote(messageText)} {txHz} 0 0 0 1 99",
            "JT4" => $"{Quote(messageText)} 4A {txHz} 0 0 0 1 99",
            "JT65" => $"-m A -n 1 -F {txHz} -d 0 -D 0 -t 0 -f 1 -s 99 -M {Quote(messageText)}",
            "JT9" => $"{Quote(messageText)} 9A {txHz} 0 0 0 1 99",
            "MSK144" => $"{Quote(messageText)} {Math.Max(15, (int)Math.Round(cycleLengthSeconds)).ToString(CultureInfo.InvariantCulture)} {txHz} 15 99 1",
            "Q65" => $"{Quote(messageText)} A {txHz} 0 0 0 1 {Math.Max(15, (int)Math.Round(cycleLengthSeconds)).ToString(CultureInfo.InvariantCulture)} 1 1 99",
            "FST4" => $"{Quote(messageText)} {Math.Max(15, (int)Math.Round(cycleLengthSeconds)).ToString(CultureInfo.InvariantCulture)} {txHz} 0 0 0 1 99 F",
            "FST4W" => $"{Quote(messageText)} {Math.Max(120, (int)Math.Round(cycleLengthSeconds)).ToString(CultureInfo.InvariantCulture)} {txHz} 0 0 0 1 99 T",
            "WSPR" => $"-f 0 -s 99 -o staged.c2 {Quote(messageText)}",
            _ => throw new InvalidOperationException($"Unsupported mode {modeLabel}"),
        };
    }

    private static string GetGeneratedArtifactPattern(string modeLabel) => modeLabel switch
    {
        "WSPR" => "*.c2",
        _ => "*.wav",
    };

    private static bool IsSuccessfulExitCode(string modeLabel, int exitCode) => modeLabel switch
    {
        "WSPR" => exitCode is 0 or 1,
        _ => exitCode == 0,
    };

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string? ResolveTool(string preferredBinRoot, string fallbackBinRoot, string fileName)
    {
        var preferredPath = Path.Combine(preferredBinRoot, fileName);
        if (File.Exists(preferredPath))
        {
            return preferredPath;
        }

        var fallbackPath = Path.Combine(fallbackBinRoot, fileName);
        return File.Exists(fallbackPath) ? fallbackPath : null;
    }

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var reader = new StringReader(text);
        return reader.ReadLine()?.Trim();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static Pcm16AudioClip? TryLoadPreparedClip(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) != "RIFF")
        {
            return null;
        }

        _ = reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
        {
            return null;
        }

        short formatTag = 0;
        short channels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? data = null;

        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            if (chunkSize < 0 || reader.BaseStream.Position + chunkSize > reader.BaseStream.Length)
            {
                return null;
            }

            if (chunkId == "fmt ")
            {
                formatTag = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
                var remaining = chunkSize - 16;
                if (remaining > 0)
                {
                    reader.ReadBytes(remaining);
                }
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }
            else
            {
                reader.ReadBytes(chunkSize);
            }

            if ((chunkSize & 1) != 0 && reader.BaseStream.Position < reader.BaseStream.Length)
            {
                reader.ReadByte();
            }
        }

        if (formatTag != 1 || channels <= 0 || sampleRate <= 0 || bitsPerSample != 16 || data is null)
        {
            return null;
        }

        data = TrimTrailingSilence(data, sampleRate, channels);
        return new Pcm16AudioClip(data, sampleRate, channels);
    }

    private static byte[] TrimTrailingSilence(byte[] data, int sampleRate, int channels)
    {
        if (data.Length < 2 || sampleRate <= 0 || channels <= 0)
        {
            return data;
        }

        const short silenceThreshold = 8;
        const int keepTailMs = 120;

        var frameSize = channels * 2;
        if (frameSize <= 0 || data.Length < frameSize)
        {
            return data;
        }

        var lastActiveFrameStart = -1;
        for (var offset = data.Length - frameSize; offset >= 0; offset -= frameSize)
        {
            var active = false;
            for (var channel = 0; channel < channels; channel++)
            {
                var sampleOffset = offset + (channel * 2);
                var sample = BitConverter.ToInt16(data, sampleOffset);
                if (Math.Abs(sample) > silenceThreshold)
                {
                    active = true;
                    break;
                }
            }

            if (active)
            {
                lastActiveFrameStart = offset;
                break;
            }
        }

        if (lastActiveFrameStart < 0)
        {
            return data;
        }

        var keepTailBytes = Math.Max(frameSize, (int)Math.Ceiling(sampleRate * channels * 2 * (keepTailMs / 1000.0)));
        var trimmedLength = Math.Min(data.Length, lastActiveFrameStart + frameSize + keepTailBytes);
        if (trimmedLength >= data.Length)
        {
            return data;
        }

        var trimmed = new byte[trimmedLength];
        Buffer.BlockCopy(data, 0, trimmed, 0, trimmedLength);
        return trimmed;
    }

    private static class Js8FrameEncoder
    {
        private const string Alphabet72 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-+/?.";
        private const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ /@";
        private const uint NBaseCall = 37 * 36 * 10 * 27 * 27 * 27;
        private const ushort NMaxGrid = (1 << 15) - 1;
        private const int FrameDirected = 3;
        private const int FrameHeartbeat = 0;
        private const int Js8Call = 0;
        private const int Js8CallFirst = 1;
        private const int Js8CallLast = 2;

        public sealed record Js8PreparedFrame(string Frame, string Text, int Bits);

        private static readonly Dictionary<char, string> HuffTable = new()
        {
            [' '] = "01",
            ['E'] = "100",
            ['T'] = "1101",
            ['A'] = "0011",
            ['O'] = "11111",
            ['I'] = "11100",
            ['N'] = "10111",
            ['S'] = "10100",
            ['H'] = "00011",
            ['R'] = "00000",
            ['D'] = "111011",
            ['L'] = "110011",
            ['C'] = "110001",
            ['U'] = "101101",
            ['M'] = "101011",
            ['W'] = "001011",
            ['F'] = "001001",
            ['G'] = "000101",
            ['Y'] = "000011",
            ['P'] = "1111011",
            ['B'] = "1111001",
            ['.'] = "1110100",
            ['V'] = "1100101",
            ['K'] = "1100100",
            ['-'] = "1100001",
            ['+'] = "1100000",
            ['?'] = "1011001",
            ['!'] = "1011000",
            ['"'] = "1010101",
            ['X'] = "1010100",
            ['0'] = "0010101",
            ['J'] = "0010100",
            ['1'] = "0010001",
            ['Q'] = "0010000",
            ['2'] = "0001001",
            ['Z'] = "0001000",
            ['3'] = "0000101",
            ['5'] = "0000100",
            ['4'] = "11110101",
            ['9'] = "11110100",
            ['8'] = "11110001",
            ['6'] = "11110000",
            ['7'] = "11101011",
            ['/'] = "11101010",
        };

        private static readonly Dictionary<string, uint> BaseCalls = new(StringComparer.Ordinal)
        {
            ["<....>"] = NBaseCall + 1,
            ["@ALLCALL"] = NBaseCall + 2,
            ["@JS8NET"] = NBaseCall + 3,
            ["@DX/NA"] = NBaseCall + 4,
            ["@DX/SA"] = NBaseCall + 5,
            ["@DX/EU"] = NBaseCall + 6,
            ["@DX/AS"] = NBaseCall + 7,
            ["@DX/AF"] = NBaseCall + 8,
            ["@DX/OC"] = NBaseCall + 9,
            ["@DX/AN"] = NBaseCall + 10,
            ["@CQ"] = NBaseCall + 44,
            ["@HB"] = NBaseCall + 45,
        };

        private static readonly Dictionary<string, int> DirectedCommands = new(StringComparer.Ordinal)
        {
            ["SNR?"] = 0,
            ["?"] = 0,
            ["DIT DIT"] = 1,
            ["NACK"] = 2,
            ["HEARING?"] = 3,
            ["GRID?"] = 4,
            [">"] = 5,
            ["STATUS?"] = 6,
            ["STATUS"] = 7,
            ["HEARING"] = 8,
            ["MSG"] = 9,
            ["MSG TO:"] = 10,
            ["QUERY"] = 11,
            ["QUERY MSGS"] = 12,
            ["QUERY MSGS?"] = 12,
            ["QUERY CALL"] = 13,
            ["ACK"] = 14,
            ["GRID"] = 15,
            ["INFO?"] = 16,
            ["INFO"] = 17,
            ["FB"] = 18,
            ["HW CPY?"] = 19,
            ["SK"] = 20,
            ["RR"] = 21,
            ["QSL?"] = 22,
            ["QSL"] = 23,
            ["CMD"] = 24,
            ["SNR"] = 25,
            ["NO"] = 26,
            ["YES"] = 27,
            ["73"] = 28,
            ["HEARTBEAT SNR"] = 29,
            ["AGN?"] = 30,
            [" "] = 31,
        };

        private static readonly string[] DirectedCommandOrder =
        [
            "HEARTBEAT SNR",
            "QUERY MSGS?",
            "QUERY MSGS",
            "QUERY CALL",
            "MSG TO:",
            "DIT DIT",
            "HEARING?",
            "STATUS?",
            "HW CPY?",
            "SNR?",
            "GRID?",
            "INFO?",
            "QSL?",
            "AGN?",
            "STATUS",
            "HEARING",
            "QUERY",
            "NACK",
            "ACK",
            "GRID",
            "INFO",
            "MSG",
            "CMD",
            "SNR",
            "QSL",
            "YES",
            "NO",
            "FB",
            "SK",
            "RR",
            "73",
            "?",
            ">",
        ];

        public static IReadOnlyList<Js8PreparedFrame> BuildMessageFrames(string input)
        {
            var normalized = NormalizeMessage(input);
            var frames = new List<Js8PreparedFrame>();
            var heartbeat = PackHeartbeatMessage(normalized, out var heartbeatText);
            if (!string.IsNullOrWhiteSpace(heartbeat))
            {
                frames.Add(new Js8PreparedFrame(heartbeat, heartbeatText, Js8Call));
                normalized = TrimPackedPrefix(normalized, heartbeatText);
            }

            var directed = PackDirectedMessage(normalized, out var directedText);
            if (!string.IsNullOrWhiteSpace(directed))
            {
                frames.Add(new Js8PreparedFrame(directed, directedText, Js8Call));
                normalized = TrimPackedPrefix(normalized, directedText);
            }

            normalized = NormalizeDataMessage(normalized);
            while (!string.IsNullOrWhiteSpace(normalized))
            {
                var frame = PackDataMessage(normalized, out var frameText);
                if (string.IsNullOrWhiteSpace(frame) || string.IsNullOrWhiteSpace(frameText))
                {
                    break;
                }

                frames.Add(new Js8PreparedFrame(frame, frameText, Js8Call));
                normalized = TrimPackedPrefix(normalized, frameText);
            }

            ApplyFirstLastFlags(frames);
            return frames;
        }

        public static string PackMessageFrame(string input, out string packedText, out int frameBits)
        {
            var frames = BuildMessageFrames(input);
            if (frames.Count == 0)
            {
                packedText = string.Empty;
                frameBits = Js8Call;
                return string.Empty;
            }

            packedText = frames[0].Text;
            frameBits = frames[0].Bits;
            return frames[0].Frame;
        }

        private static string PackHeartbeatMessage(string input, out string packedText)
        {
            packedText = string.Empty;
            var normalized = NormalizeMessage(input);
            var colonIndex = normalized.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 1 || colonIndex >= normalized.Length - 1)
            {
                return string.Empty;
            }

            var callsign = normalized[..colonIndex].Trim();
            var remainder = normalized[(colonIndex + 1)..].TrimStart();
            var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return string.Empty;
            }

            var index = parts[0] is "@HB" or "@ALLCALL" ? 1 : 0;
            if (index >= parts.Length)
            {
                return string.Empty;
            }

            var type = parts[index];
            if (type != "HB" && type != "HEARTBEAT")
            {
                return string.Empty;
            }

            var grid = index + 1 < parts.Length ? parts[index + 1] : string.Empty;
            var packedGrid = NMaxGrid;
            if (IsFourCharacterGrid(grid))
            {
                packedGrid = PackGrid(grid);
            }

            var frame = PackCompoundFrame(callsign, FrameHeartbeat, packedGrid, bits3: 0);
            if (string.IsNullOrWhiteSpace(frame))
            {
                return string.Empty;
            }

            packedText = string.IsNullOrWhiteSpace(grid)
                ? $"{callsign}: @HB HEARTBEAT"
                : $"{callsign}: @HB HEARTBEAT {grid[..4].ToUpperInvariant()}";
            return frame;
        }

        private static string PackCompoundFrame(string callsign, byte type, ushort num, byte bits3)
        {
            var packedCallsign = PackAlphaNumeric50(callsign);
            if (packedCallsign == 0)
            {
                return string.Empty;
            }

            var packed11 = (ushort)((num & (((1 << 11) - 1) << 5)) >> 5);
            var packed5 = (byte)(num & ((1 << 5) - 1));
            var packed8 = (byte)((packed5 << 3) | (bits3 & 0x07));

            var bits = new List<bool>(64);
            AppendBits(bits, type, 3);
            AppendBits(bits, packedCallsign, 50);
            AppendBits(bits, packed11, 11);
            return Pack72Bits(BitsToUInt64(bits, 0, 64), packed8);
        }

        private static ulong PackAlphaNumeric50(string value)
        {
            var word = new string(value
                .ToUpperInvariant()
                .Where(ch => Alphanumeric.Contains(ch, StringComparison.Ordinal))
                .ToArray());
            if (word.Length > 3 && word[3] != '/')
            {
                word = word.Insert(3, " ");
            }

            if (word.Length > 7 && word[7] != '/')
            {
                word = word.Insert(7, " ");
            }

            if (word.Length < 11)
            {
                word = word.PadRight(11);
            }
            else if (word.Length > 11)
            {
                word = word[..11];
            }

            var a = Pow38(8) * 4UL * (ulong)Alphanumeric.IndexOf(word[0], StringComparison.Ordinal);
            var b = Pow38(7) * 4UL * (ulong)Alphanumeric.IndexOf(word[1], StringComparison.Ordinal);
            var c = Pow38(6) * 4UL * (ulong)Alphanumeric.IndexOf(word[2], StringComparison.Ordinal);
            var d = Pow38(6) * 2UL * (ulong)(word[3] == '/' ? 1 : 0);
            var e = Pow38(5) * 2UL * (ulong)Alphanumeric.IndexOf(word[4], StringComparison.Ordinal);
            var f = Pow38(4) * 2UL * (ulong)Alphanumeric.IndexOf(word[5], StringComparison.Ordinal);
            var g = Pow38(3) * 2UL * (ulong)Alphanumeric.IndexOf(word[6], StringComparison.Ordinal);
            var h = Pow38(3) * (ulong)(word[7] == '/' ? 1 : 0);
            var i = Pow38(2) * (ulong)Alphanumeric.IndexOf(word[8], StringComparison.Ordinal);
            var j = 38UL * (ulong)Alphanumeric.IndexOf(word[9], StringComparison.Ordinal);
            var k = (ulong)Alphanumeric.IndexOf(word[10], StringComparison.Ordinal);

            return a + b + c + d + e + f + g + h + i + j + k;
        }

        private static ulong Pow38(int exponent)
        {
            var value = 1UL;
            for (var i = 0; i < exponent; i++)
            {
                value *= 38UL;
            }

            return value;
        }

        private static bool IsFourCharacterGrid(string value) =>
            value.Length >= 4
            && value[0] is >= 'A' and <= 'R'
            && value[1] is >= 'A' and <= 'R'
            && value[2] is >= '0' and <= '9'
            && value[3] is >= '0' and <= '9';

        private static ushort PackGrid(string value)
        {
            var grid = value[..4].ToUpperInvariant();
            var longitude = 180 - (20 * (grid[0] - 'A')) - (2 * (grid[2] - '0'));
            var latitude = -90 + (10 * (grid[1] - 'A')) + (grid[3] - '0');
            return (ushort)((((longitude + 180) / 2) * 180) + (latitude + 90));
        }

        private static string TrimPackedPrefix(string text, string packedPrefix)
        {
            if (packedPrefix.Length >= text.Length)
            {
                return string.Empty;
            }

            return NormalizeMessage(text[packedPrefix.Length..]);
        }

        private static void ApplyFirstLastFlags(List<Js8PreparedFrame> frames)
        {
            if (frames.Count == 0)
            {
                return;
            }

            frames[0] = frames[0] with { Bits = frames[0].Bits | Js8CallFirst };
            frames[^1] = frames[^1] with { Bits = frames[^1].Bits | Js8CallLast };
        }

        private static string PackDirectedMessage(string input, out string packedText)
        {
            packedText = string.Empty;
            var normalized = NormalizeMessage(input);
            var colonIndex = normalized.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 1 || colonIndex >= normalized.Length - 1)
            {
                return string.Empty;
            }

            var from = normalized[..colonIndex].Trim();
            var remainder = normalized[(colonIndex + 1)..].TrimStart();
            var parts = remainder.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return string.Empty;
            }

            var to = parts[0];
            var tail = parts.Length > 1 ? parts[1] : string.Empty;
            if (!TryMatchDirectedCommand(tail, out var command, out var commandLength))
            {
                return string.Empty;
            }

            var numText = tail[commandLength..].Trim();
            var num = PackNum(numText, out var numOk);
            if (!numOk)
            {
                num = 0;
            }

            var packedFrom = PackCallsign(from, out var portableFrom);
            var packedTo = PackCallsign(to, out var portableTo);
            if (packedFrom == 0 || packedTo == 0)
            {
                return string.Empty;
            }

            var packedCmd = DirectedCommands[command] % 32;
            var packedExtra = (((portableFrom ? 1 : 0) << 7) + ((portableTo ? 1 : 0) << 6) + num) & 0xFF;
            var bits = new List<bool>(64);
            AppendBits(bits, FrameDirected, 3);
            AppendBits(bits, packedFrom, 28);
            AppendBits(bits, packedTo, 28);
            AppendBits(bits, (ulong)packedCmd, 5);

            packedText = $"{from}: {to} {command}{(numOk ? $" {numText}" : string.Empty)}";
            return Pack72Bits(BitsToUInt64(bits, 0, 64), (byte)packedExtra);
        }

        private static bool TryMatchDirectedCommand(string text, out string command, out int length)
        {
            var normalized = NormalizeMessage(text);
            foreach (var candidate in DirectedCommandOrder)
            {
                if (normalized.Equals(candidate, StringComparison.Ordinal)
                    || normalized.StartsWith(candidate + " ", StringComparison.Ordinal))
                {
                    command = candidate;
                    length = candidate.Length;
                    return true;
                }
            }

            command = string.Empty;
            length = 0;
            return false;
        }

        private static string PackDataMessage(string input, out string packedText)
        {
            packedText = string.Empty;
            var text = NormalizeDataMessage(input);
            if (text.Length == 0)
            {
                return string.Empty;
            }

            var bits = new List<bool>(72)
            {
                true,
                false,
            };

            foreach (var ch in text)
            {
                var charBits = HuffTable[ch];
                if (bits.Count + charBits.Length >= 72)
                {
                    break;
                }

                foreach (var bit in charBits)
                {
                    bits.Add(bit == '1');
                }

                packedText += ch;
            }

            if (packedText.Length == 0)
            {
                return string.Empty;
            }

            var pad = 72 - bits.Count;
            for (var i = 0; i < pad; i++)
            {
                bits.Add(i != 0);
            }

            var value = BitsToUInt64(bits, 0, 64);
            var rem = (byte)BitsToUInt64(bits, 64, 8);
            return Pack72Bits(value, rem);
        }

        private static uint PackCallsign(string value, out bool portable)
        {
            portable = false;
            var callsign = value.ToUpperInvariant().Trim();
            if (BaseCalls.TryGetValue(callsign, out var baseCall))
            {
                return baseCall;
            }

            if (callsign.EndsWith("/P", StringComparison.Ordinal))
            {
                callsign = callsign[..^2];
                portable = true;
            }

            if (callsign.StartsWith("3DA0", StringComparison.Ordinal))
            {
                callsign = "3D0" + callsign[4..];
            }

            if (callsign.Length > 2 && callsign.StartsWith("3X", StringComparison.Ordinal) && callsign[2] is >= 'A' and <= 'Z')
            {
                callsign = "Q" + callsign[2..];
            }

            if (callsign.Length is < 2 or > 6)
            {
                return 0;
            }

            var permutations = new List<string> { callsign };
            if (callsign.Length == 2) permutations.Add(" " + callsign + "   ");
            if (callsign.Length == 3)
            {
                permutations.Add(" " + callsign + "  ");
                permutations.Add(callsign + "   ");
            }
            if (callsign.Length == 4)
            {
                permutations.Add(" " + callsign + " ");
                permutations.Add(callsign + "  ");
            }
            if (callsign.Length == 5)
            {
                permutations.Add(" " + callsign);
                permutations.Add(callsign + " ");
            }

            foreach (var candidate in permutations)
            {
                if (candidate.Length != 6
                    || !IsPackableCallChar(candidate[0], allowSpace: true, allowDigit: true)
                    || !IsPackableCallChar(candidate[1], allowSpace: false, allowDigit: true)
                    || candidate[2] is < '0' or > '9'
                    || !IsPackableCallChar(candidate[3], allowSpace: true, allowDigit: false)
                    || !IsPackableCallChar(candidate[4], allowSpace: true, allowDigit: false)
                    || !IsPackableCallChar(candidate[5], allowSpace: true, allowDigit: false))
                {
                    continue;
                }

                var packed = (uint)Alphanumeric.IndexOf(candidate[0], StringComparison.Ordinal);
                packed = (36 * packed) + (uint)Alphanumeric.IndexOf(candidate[1], StringComparison.Ordinal);
                packed = (10 * packed) + (uint)Alphanumeric.IndexOf(candidate[2], StringComparison.Ordinal);
                packed = (27 * packed) + (uint)Alphanumeric.IndexOf(candidate[3], StringComparison.Ordinal) - 10;
                packed = (27 * packed) + (uint)Alphanumeric.IndexOf(candidate[4], StringComparison.Ordinal) - 10;
                packed = (27 * packed) + (uint)Alphanumeric.IndexOf(candidate[5], StringComparison.Ordinal) - 10;
                return packed;
            }

            return 0;
        }

        private static bool IsPackableCallChar(char ch, bool allowSpace, bool allowDigit)
        {
            if (allowSpace && ch == ' ') return true;
            if (allowDigit && ch is >= '0' and <= '9') return true;
            return ch is >= 'A' and <= 'Z';
        }

        private static int PackNum(string value, out bool ok)
        {
            ok = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed);
            if (!ok)
            {
                return 0;
            }

            return Math.Clamp(parsed, -30, 31) + 31;
        }

        private static string NormalizeMessage(string input) =>
            string.Join(' ', input.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        private static string NormalizeDataMessage(string input)
        {
            var normalized = NormalizeMessage(input);
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            var chars = normalized
                .Select(ch => HuffTable.ContainsKey(ch) ? ch : ' ')
                .ToArray();
            return NormalizeMessage(new string(chars));
        }

        private static void AppendBits(List<bool> bits, ulong value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1UL) == 1UL);
            }
        }

        private static string Pack72Bits(ulong value, byte rem)
        {
            Span<char> packed = stackalloc char[12];
            var remHigh = (byte)(((value & 0x0F) << 2) | ((uint)rem >> 6));
            var remLow = (byte)(rem & 0x3F);
            value >>= 4;

            packed[11] = Alphabet72[remLow];
            packed[10] = Alphabet72[remHigh];
            for (var i = 0; i < 10; i++)
            {
                packed[9 - i] = Alphabet72[(int)(value & 0x3F)];
                value >>= 6;
            }

            return new string(packed);
        }

        private static ulong BitsToUInt64(IReadOnlyList<bool> bits, int start, int count)
        {
            ulong value = 0;
            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | (bits[start + i] ? 1UL : 0UL);
            }

            return value;
        }
    }
}
