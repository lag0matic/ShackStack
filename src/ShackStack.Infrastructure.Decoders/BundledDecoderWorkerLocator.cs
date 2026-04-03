using System.Diagnostics;

namespace ShackStack.Infrastructure.Decoders;

internal readonly record struct DecoderWorkerLaunch(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    string DisplayPath,
    bool Exists);

internal static class BundledDecoderWorkerLocator
{
    public static DecoderWorkerLaunch Resolve(string workerBaseName)
    {
        var bundledExecutable = Path.Combine(
            AppContext.BaseDirectory,
            "DecoderWorkers",
            workerBaseName,
            $"{workerBaseName}.exe");

        if (File.Exists(bundledExecutable))
        {
            return new DecoderWorkerLaunch(
                bundledExecutable,
                string.Empty,
                Path.GetDirectoryName(bundledExecutable)!,
                bundledExecutable,
                true);
        }

        var repoRoot = FindRepoRoot();
        if (repoRoot is not null)
        {
            var pythonScript = Path.Combine(
                repoRoot,
                "src",
                "ShackStack.DecoderHost",
                "Python",
                "Tools",
                $"{workerBaseName}.py");

            if (File.Exists(pythonScript))
            {
                return new DecoderWorkerLaunch(
                    "python",
                    $"\"{pythonScript}\"",
                    Path.GetDirectoryName(pythonScript)!,
                    pythonScript,
                    true);
            }
        }

        return new DecoderWorkerLaunch(
            bundledExecutable,
            string.Empty,
            Path.Combine(AppContext.BaseDirectory, "DecoderWorkers", workerBaseName),
            bundledExecutable,
            false);
    }

    public static ProcessStartInfo CreateStartInfo(DecoderWorkerLaunch launch) =>
        new()
        {
            FileName = launch.FileName,
            Arguments = launch.Arguments,
            WorkingDirectory = launch.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

    private static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ShackStack.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
