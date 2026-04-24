using Avalonia.Media.Imaging;
using System.Runtime.Versioning;

namespace ShackStack.UI.ViewModels;

internal static class SstvReplyArchiveStore
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp",
        ".png",
        ".jpg",
        ".jpeg",
    };

    public static SstvArchiveSnapshot Load(string receivedDirectory, string replyDirectory, string templateDirectory)
    {
        Directory.CreateDirectory(receivedDirectory);
        Directory.CreateDirectory(replyDirectory);
        Directory.CreateDirectory(templateDirectory);
        if (OperatingSystem.IsWindows())
        {
            EnsureStarterImages(replyDirectory);
        }

        var receivedItems = EnumerateImages(receivedDirectory)
            .Take(40)
            .Select(TryCreateImageItem)
            .OfType<SstvImageItem>()
            .ToList();

        var replyItems = EnumerateImages(replyDirectory)
            .Take(40)
            .Select(TryCreateImageItem)
            .OfType<SstvImageItem>()
            .ToList();

        var templateItems = new DirectoryInfo(templateDirectory)
            .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .Take(50)
            .Select(static file => new SstvTemplateItem(
                Path.GetFileNameWithoutExtension(file.Name),
                file.FullName,
                file.LastWriteTime))
            .ToList();

        return new SstvArchiveSnapshot(receivedItems, replyItems, templateItems);
    }

    public static SstvImageItem? TryCreateImageItem(string imagePath)
    {
        try
        {
            return TryCreateImageItem(new FileInfo(imagePath));
        }
        catch
        {
            return null;
        }
    }

    public static string ImportReplyBaseImage(string sourcePath, string replyDirectory)
    {
        var extension = Path.GetExtension(sourcePath);
        if (!SupportedImageExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Import supports BMP, PNG, JPG, and JPEG images");
        }

        Directory.CreateDirectory(replyDirectory);
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var fileName = MakeSafeFileName(sourceName, "Imported Reply Image");
        var normalizedExtension = extension.ToLowerInvariant();
        var destination = Path.Combine(replyDirectory, $"{fileName}{normalizedExtension}");
        var suffix = 1;
        while (File.Exists(destination) &&
               !string.Equals(Path.GetFullPath(destination), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            destination = Path.Combine(replyDirectory, $"{fileName}_{suffix++}{normalizedExtension}");
        }

        if (!string.Equals(Path.GetFullPath(destination), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destination, overwrite: false);
        }

        return destination;
    }

    public static string DuplicateReplyBaseImage(string sourcePath, string replyDirectory)
    {
        var baseName = $"{Path.GetFileNameWithoutExtension(sourcePath)} Copy";
        var destination = BuildUniqueFilePath(replyDirectory, baseName, Path.GetExtension(sourcePath));
        File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }

    public static string ArchiveFile(string sourcePath, string archiveDirectory)
    {
        Directory.CreateDirectory(archiveDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var baseName = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{timestamp}";
        var destination = BuildUniqueFilePath(archiveDirectory, baseName, Path.GetExtension(sourcePath));
        File.Move(sourcePath, destination);
        return destination;
    }

    public static SstvImageItem? SelectByPathOrFirst(IEnumerable<SstvImageItem> items, string? selectedPath)
        => items.FirstOrDefault(item => string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
           ?? items.FirstOrDefault();

    public static SstvTemplateItem? SelectByPathOrFirst(IEnumerable<SstvTemplateItem> items, string? selectedPath)
        => items.FirstOrDefault(item => string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
           ?? items.FirstOrDefault();

    private static IEnumerable<FileInfo> EnumerateImages(string directory)
    {
        return new DirectoryInfo(directory)
            .EnumerateFiles("*.*", SearchOption.TopDirectoryOnly)
            .Where(file => SupportedImageExtensions.Contains(file.Extension))
            .OrderByDescending(file => file.LastWriteTimeUtc);
    }

    private static SstvImageItem? TryCreateImageItem(FileInfo file)
    {
        try
        {
            return new SstvImageItem(
                Path.GetFileNameWithoutExtension(file.Name),
                file.FullName,
                file.LastWriteTime,
                new Bitmap(file.FullName));
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureStarterImages(string replyDirectory)
    {
        SstvReplyRenderer.CreateStarterImage(
            Path.Combine(replyDirectory, "ShackStack Reply Card.png"),
            "#182033",
            "#30405E",
            "SHACKSTACK SSTV",
            "Reply Card");
        SstvReplyRenderer.CreateStarterImage(
            Path.Combine(replyDirectory, "Clean Dark Reply.png"),
            "#0D111A",
            "#1D2638",
            "SSTV REPLY",
            "Compose overlays here");
    }

    private static string BuildUniqueFilePath(string directory, string baseName, string extension)
    {
        Directory.CreateDirectory(directory);
        var safeBaseName = MakeSafeFileName(baseName, "SSTV Item");
        var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.ToLowerInvariant();
        var destination = Path.Combine(directory, $"{safeBaseName}{normalizedExtension}");
        var suffix = 1;
        while (File.Exists(destination))
        {
            destination = Path.Combine(directory, $"{safeBaseName}_{suffix++}{normalizedExtension}");
        }

        return destination;
    }

    private static string MakeSafeFileName(string value, string fallback)
    {
        var safe = string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }
}
