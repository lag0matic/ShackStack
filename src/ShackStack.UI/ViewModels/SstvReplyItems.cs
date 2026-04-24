using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ShackStack.UI.ViewModels;

public sealed record SstvImageItem(string Label, string Path, DateTime Timestamp, Bitmap Bitmap);

public sealed record SstvOverlayTemplateFile(
    string Name,
    IReadOnlyList<SstvOverlayTemplateItemFile> Items,
    IReadOnlyList<SstvImageOverlayTemplateItemFile>? ImageItems = null,
    string? PresetKind = null);

public sealed record SstvOverlayTemplateItemFile(string Text, double X, double Y, double FontSize, string FontFamily, string Color);
public sealed record SstvImageOverlayTemplateItemFile(string Label, string Path, double X, double Y, double Width, double Height);
public sealed record SstvTemplateItem(string Name, string Path, DateTime Timestamp);
public sealed record SstvReplyLayoutPreset(string Label, string Kind);
public sealed record SstvArchiveSnapshot(
    IReadOnlyList<SstvImageItem> ReceivedImages,
    IReadOnlyList<SstvImageItem> ReplyImages,
    IReadOnlyList<SstvTemplateItem> LayoutTemplates);

public sealed class SstvOverlayItemViewModel : ObservableObject
{
    private string _text = "W8STR DE KE9CRR - 599!";
    private double _x = 160;
    private double _y = 210;
    private double _fontSize = 18;
    private string _fontFamilyName = "Segoe UI";
    private int _red = 245;
    private int _green = 247;
    private int _blue = 255;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    public string FontFamilyName
    {
        get => _fontFamilyName;
        set
        {
            if (SetProperty(ref _fontFamilyName, value))
            {
                OnPropertyChanged(nameof(PreviewFontFamily));
            }
        }
    }

    public int Red
    {
        get => _red;
        set
        {
            var clamped = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _red, clamped))
            {
                OnColorChanged();
            }
        }
    }

    public int Green
    {
        get => _green;
        set
        {
            var clamped = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _green, clamped))
            {
                OnColorChanged();
            }
        }
    }

    public int Blue
    {
        get => _blue;
        set
        {
            var clamped = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _blue, clamped))
            {
                OnColorChanged();
            }
        }
    }

    public string ColorHex => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public IBrush PreviewBrush => new SolidColorBrush(Color.FromRgb((byte)Red, (byte)Green, (byte)Blue));

    public FontFamily PreviewFontFamily => new(FontFamilyName);

    public void SetColorFromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return;
        }

        try
        {
            var parsed = Color.Parse(hex);
            _red = parsed.R;
            _green = parsed.G;
            _blue = parsed.B;
            OnPropertyChanged(nameof(Red));
            OnPropertyChanged(nameof(Green));
            OnPropertyChanged(nameof(Blue));
            OnColorChanged();
        }
        catch
        {
        }
    }

    private void OnColorChanged()
    {
        OnPropertyChanged(nameof(ColorHex));
        OnPropertyChanged(nameof(PreviewBrush));
    }
}

public sealed class SstvImageOverlayItemViewModel : ObservableObject
{
    private string _label = "Received image";
    private string _path = string.Empty;
    private Bitmap? _bitmap;
    private double _x = 24;
    private double _y = 24;
    private double _width = 96;
    private double _height = 76;

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public Bitmap? Bitmap
    {
        get => _bitmap;
        set => SetProperty(ref _bitmap, value);
    }

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, Math.Max(0.0, value));
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, Math.Max(0.0, value));
    }

    public double Width
    {
        get => _width;
        set
        {
            if (SetProperty(ref _width, Math.Clamp(value, 24.0, 640.0)))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            if (SetProperty(ref _height, Math.Clamp(value, 24.0, 496.0)))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string Summary => $"{Label}  |  {Width:0}x{Height:0}";

    public static SstvImageOverlayItemViewModel FromImage(SstvImageItem image, int index)
        => new()
        {
            Label = image.Label,
            Path = image.Path,
            Bitmap = image.Bitmap,
            X = 24 + (index * 14),
            Y = 24 + (index * 14),
            Width = 112,
            Height = 86,
        };
}
