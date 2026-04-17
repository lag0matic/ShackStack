using System.Diagnostics;
using System.Globalization;
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
        var processPath = ResolveProcessPath(mode);
        if (processPath is null)
        {
            return new WsjtxPreparedTransmitResult(
                false,
                $"{modeLabel} TX waveform generation is not wired yet through the installed WSJT-X tools",
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
}
