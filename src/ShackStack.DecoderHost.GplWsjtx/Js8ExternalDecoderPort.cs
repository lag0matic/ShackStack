using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ShackStack.DecoderHost.GplWsjtx;

internal sealed class Js8ExternalDecoderPort
{
    private static readonly Regex DecodeRegex = new(
        @"^(?<utc>\d{4,6})\s+(?<snr>-?\d+)\s+(?<dt>[+\-]?\d+\.\d)\s+(?<freq>-?\d+)\s+(?<sub>[ABCEI])\s+(?<message>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _decoderPath;
    private readonly string _binDirectory;
    private readonly string _workspaceDirectory;

    private Js8ExternalDecoderPort(string decoderPath)
    {
        _decoderPath = decoderPath;
        _binDirectory = Path.GetDirectoryName(decoderPath) ?? ".";
        _workspaceDirectory = Path.Combine(Path.GetTempPath(), "ShackStack-js8call-jt9");
        Directory.CreateDirectory(_workspaceDirectory);
    }

    public static Js8ExternalDecoderPort? CreateDefault()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("SHACKSTACK_JS8_JT9_PATH"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ShackStack",
                "js8call-tools",
                "runtime",
                "bin",
                "jt9.exe"),
            @"C:\Program Files\JS8Call\bin\jt9.exe",
            @"C:\Program Files (x86)\JS8Call\bin\jt9.exe",
        };

        candidates.InsertRange(1, ResolveBundledCandidates());

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return new Js8ExternalDecoderPort(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> ResolveBundledCandidates()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            yield return Path.Combine(current.FullName, "js8call-tools", "runtime", "bin", "jt9.exe");

            if (File.Exists(Path.Combine(current.FullName, "ShackStack.sln")))
            {
                yield return Path.Combine(
                    current.FullName,
                    "src",
                    "ShackStack.Desktop",
                    "js8call-tools",
                    "runtime",
                    "bin",
                    "jt9.exe");
            }

            current = current.Parent;
        }
    }

    public Jt9DecodeCycleResult DecodeCycle(
        string modeLabel,
        float[] samples,
        string? stationCallsign,
        int cycleNumber)
    {
        var utcNow = DateTime.UtcNow;
        var fileUtc = utcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
        var wavPath = Path.Combine(_workspaceDirectory, $"js8_{GetSubmodeLetter(modeLabel).ToLowerInvariant()}_{fileUtc}.wav");
        var tempPath = Path.Combine(_workspaceDirectory, "runtime");
        Directory.CreateDirectory(tempPath);

        WriteMono16BitWave(wavPath, samples, 12000);

        var args = BuildArguments(modeLabel, wavPath, tempPath, stationCallsign);
        var stopwatch = Stopwatch.StartNew();
        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _decoderPath,
                    WorkingDirectory = _binDirectory,
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
                .Select(line => ParseDecodeLine(line, _binDirectory))
                .Where(line => line is not null)
                .Cast<Jt9DecodedLine>()
                .ToList();

            var finished = stdoutLines.Any(line => line.Contains("<DecodeFinished>", StringComparison.Ordinal));
            var summary = $"JS8 cycle {cycleNumber}: {Path.GetFileName(_decoderPath)} {GetSubmodeName(modeLabel)} | {decodes.Count} decodes | {(finished ? "finished" : "no-finish")} | {stopwatch.ElapsedMilliseconds} ms";
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

    private string BuildArguments(string modeLabel, string wavPath, string tempPath, string? stationCallsign)
    {
        var arguments = new List<string>
        {
            "-8",
            "-b",
            GetSubmodeLetter(modeLabel),
            "-d",
            "3",
            "-L",
            "200",
            "-H",
            "4000",
            "-f",
            "1500",
            "-e",
            Quote(_binDirectory),
            "-a",
            Quote(tempPath),
            "-t",
            Quote(tempPath),
        };

        if (!string.IsNullOrWhiteSpace(stationCallsign))
        {
            arguments.Add("-c");
            arguments.Add(Quote(stationCallsign.Trim().ToUpperInvariant()));
        }

        arguments.Add(Quote(wavPath));
        return string.Join(" ", arguments);
    }

    public static bool IsJs8Mode(string modeLabel) =>
        modeLabel.StartsWith("JS8", StringComparison.OrdinalIgnoreCase);

    public static int GetInputSamplesPerCycle(string modeLabel) =>
        GetSubmodeName(modeLabel) switch
        {
            "Fast" => 12000 * 10,
            "Turbo" => 12000 * 6,
            "Slow" => 12000 * 28,
            _ => 12000 * 15,
        };

    private static string GetSubmodeLetter(string modeLabel) =>
        GetSubmodeName(modeLabel) switch
        {
            "Fast" => "B",
            "Turbo" => "C",
            "Slow" => "E",
            _ => "A",
        };

    private static string GetSubmodeName(string modeLabel)
    {
        if (modeLabel.Contains("Fast", StringComparison.OrdinalIgnoreCase))
        {
            return "Fast";
        }

        if (modeLabel.Contains("Turbo", StringComparison.OrdinalIgnoreCase))
        {
            return "Turbo";
        }

        if (modeLabel.Contains("Slow", StringComparison.OrdinalIgnoreCase))
        {
            return "Slow";
        }

        return "Normal";
    }

    private static Jt9DecodedLine? ParseDecodeLine(string line, string dictionaryDirectory)
    {
        if (line.Contains("<DecodeFinished>", StringComparison.Ordinal))
        {
            return null;
        }

        var match = DecodeRegex.Match(line.Trim());
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

        var modeLabel = match.Groups["sub"].Value switch
        {
            "B" => "JS8 Fast",
            "C" => "JS8 Turbo",
            "E" => "JS8 Slow",
            _ => "JS8 Normal",
        };

        return new Jt9DecodedLine(
            frequencyHz,
            snrDb,
            dtSeconds,
            NormalizeJs8Message(match.Groups["message"].Value, dictionaryDirectory),
            modeLabel);
    }

    private static string NormalizeJs8Message(string rawMessage, string dictionaryDirectory)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return string.Empty;
        }

        return Js8Varicode.DecodeFrame(rawMessage, dictionaryDirectory);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

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
            // Best-effort cleanup of temporary decoder WAVs.
        }
    }
}
