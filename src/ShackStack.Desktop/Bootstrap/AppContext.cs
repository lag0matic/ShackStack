using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Desktop.Bootstrap;

public sealed class AppContext
{
    public AppSettings Settings { get; set; } = AppSettings.Default;
    public string SettingsFilePath { get; set; } = string.Empty;
}
