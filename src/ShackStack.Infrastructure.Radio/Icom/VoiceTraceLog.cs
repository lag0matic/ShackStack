using System.Text;

namespace ShackStack.Infrastructure.Radio.Icom;

internal static class VoiceTraceLog
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShackStack",
        "logs");

    private static readonly string LogPath = Path.Combine(LogDirectory, "voice-rig.log");
    private static readonly Lock Sync = new();

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
