using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ShackStack.UI.Views;

namespace ShackStack.Desktop;

internal static class LayoutPreview
{
    public static async Task CaptureAsync(MainWindow shell, IClassicDesktopStyleApplicationLifetime lifetime, string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            // Exercise the actual desk creation and shared data context, without hardware services.
            foreach (var desk in new[] { "Voice", "CW", "Weak Signal", "JS8", "RTTY", "Keyboard", "FreeDV", "SSTV", "WeFAX", "Logbook", "CW Lab" })
            {
                var navigation = shell.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Tag as string, desk, StringComparison.Ordinal));
                navigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                navigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var target = shell.OwnedWindows.Single(window => window.IsVisible);
                foreach (var size in new[] { new Size(1440, 960), new Size(1180, 800) })
                {
                    target.Width = size.Width;
                    target.Height = size.Height;
                    await Task.Delay(150);
                    var pixels = new PixelSize((int)target.ClientSize.Width, (int)target.ClientSize.Height);
                    if (pixels.Width <= 0 || pixels.Height <= 0)
                        throw new InvalidOperationException($"{desk}: empty layout bounds");
                    using var bitmap = new RenderTargetBitmap(pixels, new Vector(96, 96));
                    bitmap.Render(target);
                    var path = Path.Combine(outputDirectory, $"{desk.Replace(' ', '-')}-{(int)size.Width}.png");
                    bitmap.Save(path);
                    Console.WriteLine(path);
                }
                target.Close();
            }
            lifetime.Shutdown(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            lifetime.Shutdown(1);
        }
    }
}
