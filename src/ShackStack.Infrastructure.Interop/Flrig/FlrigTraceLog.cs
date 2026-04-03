using System.Text;

namespace ShackStack.Infrastructure.Interop.Flrig;

internal static class FlrigTraceLog
{
    private static readonly object Gate = new();
    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShackStack", "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "flrig-trace.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
