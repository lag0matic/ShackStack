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
        => ResolvePreferred(workerBaseName);

    public static DecoderWorkerLaunch ResolvePreferred(params string[] workerBaseNames)
    {
        foreach (var workerBaseName in workerBaseNames)
        {
            var environmentOverride = ResolveEnvironmentOverride(workerBaseName);
            if (environmentOverride.Exists)
            {
                return environmentOverride;
            }

            var repoRoot = FindRepoRoot();
            if (repoRoot is not null)
            {
                var localDotnetWorker = ResolveLocalDotnetWorker(repoRoot, workerBaseName);
                if (localDotnetWorker.Exists)
                {
                    return localDotnetWorker;
                }
            }

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
        }

        var fallbackName = workerBaseNames.FirstOrDefault() ?? "worker";
        return new DecoderWorkerLaunch(
            Path.Combine(AppContext.BaseDirectory, "DecoderWorkers", fallbackName, $"{fallbackName}.exe"),
            string.Empty,
            Path.Combine(AppContext.BaseDirectory, "DecoderWorkers", fallbackName),
            fallbackName,
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

    private static DecoderWorkerLaunch ResolveEnvironmentOverride(string workerBaseName)
    {
        var envKey = $"SHACKSTACK_{workerBaseName.ToUpperInvariant().Replace("-", "_")}_PATH";
        var overridePath = Environment.GetEnvironmentVariable(envKey);
        if (string.IsNullOrWhiteSpace(overridePath))
        {
            return default;
        }

        overridePath = Environment.ExpandEnvironmentVariables(overridePath.Trim());
        if (!File.Exists(overridePath))
        {
            return default;
        }

        return new DecoderWorkerLaunch(
            overridePath,
            string.Empty,
            Path.GetDirectoryName(overridePath) ?? AppContext.BaseDirectory,
            overridePath,
            true);
    }

    private static DecoderWorkerLaunch ResolveLocalDotnetWorker(string repoRoot, string workerBaseName)
    {
        if (!string.Equals(workerBaseName, "wsjtx_gpl_sidecar", StringComparison.OrdinalIgnoreCase))
        {
            return default;
        }

        var projectPath = Path.Combine(
            repoRoot,
            "src",
            "ShackStack.DecoderHost.GplWsjtx",
            "ShackStack.DecoderHost.GplWsjtx.csproj");

        if (!File.Exists(projectPath))
        {
            return default;
        }

        var localExe = Path.Combine(
            repoRoot,
            "src",
            "ShackStack.DecoderHost.GplWsjtx",
            "bin",
            "Debug",
            "net9.0",
            "ShackStack.DecoderHost.GplWsjtx.exe");

        if (File.Exists(localExe))
        {
            return new DecoderWorkerLaunch(
                localExe,
                string.Empty,
                Path.GetDirectoryName(localExe)!,
                localExe,
                true);
        }

        return new DecoderWorkerLaunch(
            "dotnet",
            $"run --project \"{projectPath}\" --no-launch-profile",
            Path.GetDirectoryName(projectPath)!,
            projectPath,
            true);
    }
}
