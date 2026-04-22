using System.Diagnostics;

namespace ShackStack.Infrastructure.Decoders;

internal static class DecoderHostProcessCleanup
{
    public static void Shutdown(
        Process? process,
        StreamWriter? stdin,
        Task? stdoutTask,
        Task? stderrTask,
        SemaphoreSlim? writeGate,
        int shutdownWaitMs = 1000)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                TrySendShutdown(stdin, writeGate);

                if (!process.WaitForExit(shutdownWaitMs))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(shutdownWaitMs);
                }
            }
        }
        catch
        {
            try
            {
                if (process is not null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(shutdownWaitMs);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
        finally
        {
            TryWait(stdoutTask, shutdownWaitMs);
            TryWait(stderrTask, shutdownWaitMs);

            try
            {
                stdin?.Dispose();
            }
            catch
            {
                // ignored
            }

            try
            {
                process?.Dispose();
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void TrySendShutdown(StreamWriter? stdin, SemaphoreSlim? writeGate)
    {
        if (stdin is null)
        {
            return;
        }

        var gateHeld = false;
        try
        {
            if (writeGate is not null)
            {
                gateHeld = writeGate.Wait(250);
            }

            if (writeGate is null || gateHeld)
            {
                stdin.WriteLine("{\"type\":\"shutdown\"}");
                stdin.Flush();
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            if (gateHeld)
            {
                try
                {
                    writeGate?.Release();
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    private static void TryWait(Task? task, int timeoutMs)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            task.Wait(timeoutMs);
        }
        catch
        {
            // ignored
        }
    }
}
