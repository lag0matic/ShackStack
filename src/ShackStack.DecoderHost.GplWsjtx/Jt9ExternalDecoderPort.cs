using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ShackStack.DecoderHost.GplWsjtx;

internal sealed class Jt9ExternalDecoderPort
{
    private static readonly Regex FtFamilyRegex = new(
        @"^(?<utc>\d{4,6})\s*(?<snr>-?\d+)\s*(?<dt>[+\-]?\d+\.\d)\s*(?<freq>-?\d+)\s+(?<sep>[@~+:`]|[$#]{1,2})\s+(?<message>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _jt9Path;
    private readonly string? _wsprdPath;
    private readonly string _binDirectory;
    private readonly string _workspaceDirectory;

    private Jt9ExternalDecoderPort(string jt9Path, string? wsprdPath)
    {
        _jt9Path = jt9Path;
        _wsprdPath = wsprdPath;
        _binDirectory = Path.GetDirectoryName(jt9Path) ?? ".";
        _workspaceDirectory = Path.Combine(Path.GetTempPath(), "ShackStack-wsjtx-jt9");
        Directory.CreateDirectory(_workspaceDirectory);
    }

    public static Jt9ExternalDecoderPort? CreateDefault()
    {
        var bundledBin = Path.Combine(
            AppContext.BaseDirectory,
            "wsjtx-tools",
            "runtime",
            "bin",
            "jt9.exe");
        var localBin = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "ShackStack",
            "wsjtx-tools",
            "runtime",
            "bin",
            "jt9.exe");
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("SHACKSTACK_JT9_PATH"),
            bundledBin,
            localBin,
            @"C:\WSJT\wsjtx\bin\jt9.exe",
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                var wsprdPath = Path.Combine(Path.GetDirectoryName(candidate) ?? ".", "wsprd.exe");
                return new Jt9ExternalDecoderPort(candidate, File.Exists(wsprdPath) ? wsprdPath : null);
            }
        }

        return null;
    }

    public Jt9DecodeCycleResult DecodeCycle(
        string modeLabel,
        float[] samples,
        string? stationCallsign,
        string? stationGridSquare,
        int cycleNumber)
    {
        var utcNow = DateTime.UtcNow;
        var fileUtc = utcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
        var modePrefix = Regex.Replace(modeLabel.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_");
        if (string.IsNullOrWhiteSpace(modePrefix))
        {
            modePrefix = "wsjtx";
        }
        var wavPath = Path.Combine(_workspaceDirectory, $"{modePrefix}_{fileUtc}.wav");
        var tempPath = Path.Combine(_workspaceDirectory, "runtime");
        Directory.CreateDirectory(tempPath);

        WriteMono16BitWave(wavPath, samples, 12000);

        var processPath = ResolveProcessPath(modeLabel);
        if (processPath is null)
        {
            return new Jt9DecodeCycleResult([], [], [], $"{modeLabel} cycle {cycleNumber}: decoder binary missing");
        }

        var args = BuildArguments(modeLabel, wavPath, tempPath, stationCallsign, stationGridSquare);
        var stopwatch = Stopwatch.StartNew();
        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = processPath,
                    WorkingDirectory = Path.GetDirectoryName(processPath) ?? _binDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    Arguments = args,
                },
            };

            process.Start();
            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    stdoutLines.Add(line);
                }
            }

            while (!process.StandardError.EndOfStream)
            {
                var line = process.StandardError.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    stderrLines.Add(line);
                }
            }

            process.WaitForExit();
            stopwatch.Stop();

            var decodes = stdoutLines
                .Select(line => ParseDecodeLine(modeLabel, line))
                .Where(line => line is not null)
                .Cast<Jt9DecodedLine>()
                .ToList();

            var finished = stdoutLines.Any(line => line.Contains("<DecodeFinished>", StringComparison.Ordinal));
            var decoderName = Path.GetFileName(processPath);
            var summary = $"{modeLabel} cycle {cycleNumber}: {decoderName} | {decodes.Count} decodes | {(finished ? "finished" : "no-finish")} | {stopwatch.ElapsedMilliseconds} ms";
            if (stderrLines.Count > 0)
            {
                summary += $" | stderr {stderrLines[0]}";
            }

            return new Jt9DecodeCycleResult(decodes, stdoutLines, stderrLines, summary);
        }
        finally
        {
            TryDelete(wavPath);
        }
    }

    private string? ResolveProcessPath(string modeLabel) =>
        string.Equals(modeLabel, "WSPR", StringComparison.OrdinalIgnoreCase)
            ? _wsprdPath
            : _jt9Path;

    private string BuildArguments(string modeLabel, string wavPath, string tempPath, string? stationCallsign, string? stationGridSquare)
    {
        if (string.Equals(modeLabel, "WSPR", StringComparison.OrdinalIgnoreCase))
        {
            var wsprArgs = new List<string>
            {
                "-a", Quote(tempPath),
                "-d",
                "-o", "4",
                "-f", "14.0956",
                Quote(wavPath),
            };
            return string.Join(" ", wsprArgs);
        }

        var arguments = new List<string>();
        switch (modeLabel.Trim().ToUpperInvariant())
        {
            case "FT8":
                arguments.Add("-8");
                break;
            case "FT4":
                arguments.Add("-5");
                break;
            case "Q65":
                arguments.Add("-3");
                break;
            case "FST4":
                arguments.Add("-7");
                break;
            case "FST4W":
                arguments.Add("-W");
                break;
            case "JT65":
                arguments.Add("-6");
                break;
            case "JT9":
                arguments.Add("-9");
                break;
            case "JT4":
                arguments.Add("-4");
                break;
            case "MSK144":
                arguments.Add("-k");
                break;
            default:
                arguments.Add("-8");
                break;
        }

        arguments.AddRange([
            "-d", "3",
            "-Q", "1",
            "-L", "200",
            "-H", "4000",
            "-f", "1500",
            "-e", Quote(_binDirectory),
            "-a", Quote(tempPath),
            "-t", Quote(tempPath),
        ]);

        if (!string.IsNullOrWhiteSpace(stationCallsign))
        {
            arguments.Add("-c");
            arguments.Add(Quote(stationCallsign.Trim().ToUpperInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(stationGridSquare))
        {
            arguments.Add("-G");
            arguments.Add(Quote(stationGridSquare.Trim().ToUpperInvariant()));
        }

        arguments.Add(Quote(wavPath));
        return string.Join(" ", arguments);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static Jt9DecodedLine? ParseDecodeLine(string requestedMode, string line)
    {
        if (line.Contains("<DecodeFinished>", StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(requestedMode, "WSPR", StringComparison.OrdinalIgnoreCase))
        {
            return ParseWsprLine(line);
        }

        var match = FtFamilyRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups["snr"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var snrDb))
        {
            return null;
        }

        if (!double.TryParse(match.Groups["dt"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dtSeconds))
        {
            return null;
        }

        if (!int.TryParse(match.Groups["freq"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frequencyHz))
        {
            return null;
        }

        var normalizedMode = NormalizeModeFromSeparator(requestedMode, match.Groups["sep"].Value);
        return new Jt9DecodedLine(
            frequencyHz,
            snrDb,
            dtSeconds,
            match.Groups["message"].Value.Trim(),
            normalizedMode);
    }

    private static string NormalizeModeFromSeparator(string requestedMode, string separator) =>
        separator switch
        {
            "~" => "FT8",
            "+" => "FT4",
            "@" => "JT9",
            "`" => "FST4",
            ":" => "Q65",
            "$" or "#" => requestedMode.Equals("JT4", StringComparison.OrdinalIgnoreCase) ? "JT4" : "JT65",
            _ => requestedMode.ToUpperInvariant(),
        };

    private static Jt9DecodedLine? ParseWsprLine(string line)
    {
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 7)
        {
            return null;
        }

        if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var snrDb))
        {
            return null;
        }

        if (!double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var dtSeconds))
        {
            return null;
        }

        var frequencyHz = 0;
        if (double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz))
        {
            frequencyHz = (int)Math.Round(mhz * 1_000_000, MidpointRounding.AwayFromZero);
        }

        var messageStart = Math.Min(5, fields.Length - 1);
        var messageText = string.Join(" ", fields.Skip(messageStart));
        return new Jt9DecodedLine(frequencyHz, snrDb, dtSeconds, messageText, "WSPR");
    }

    private static void WriteMono16BitWave(string path, float[] samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);

        var dataLength = samples.Length * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        foreach (var sample in samples)
        {
            var clamped = Math.Clamp(sample, -1f, 1f);
            writer.Write((short)Math.Round(clamped * short.MaxValue, MidpointRounding.AwayFromZero));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

internal sealed record Jt9DecodeCycleResult(
    IReadOnlyList<Jt9DecodedLine> Decodes,
    IReadOnlyList<string> StdoutLines,
    IReadOnlyList<string> StderrLines,
    string Summary);

internal sealed record Jt9DecodedLine(
    int FrequencyHz,
    int SnrDb,
    double DtSeconds,
    string MessageText,
    string ModeLabel);
