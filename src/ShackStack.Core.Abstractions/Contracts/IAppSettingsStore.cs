using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IAppSettingsStore
{
    string SettingsFilePath { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
