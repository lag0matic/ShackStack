using ShackStack.Core.Abstractions.Models;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrush = System.Drawing.SolidBrush;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;
using DrawingInterpolationMode = System.Drawing.Drawing2D.InterpolationMode;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingStringAlignment = System.Drawing.StringAlignment;
using DrawingStringFormat = System.Drawing.StringFormat;
using DrawingTextRenderingHint = System.Drawing.Text.TextRenderingHint;

namespace ShackStack.UI.ViewModels;

internal static class SstvReplyRenderer
{
    [SupportedOSPlatform("windows")]
    public static byte[] RenderRgb24(
        string baseImagePath,
        IReadOnlyList<SstvOverlayItemViewModel> overlays,
        IReadOnlyList<SstvImageOverlayItemViewModel> imageOverlays,
        string outputImagePath,
        out int width,
        out int height)
    {
        using var source = new DrawingBitmap(baseImagePath);
        width = source.Width;
        height = source.Height;
        using var composed = new DrawingBitmap(width, height, DrawingPixelFormat.Format24bppRgb);
        using (var graphics = DrawingGraphics.FromImage(composed))
        {
            graphics.Clear(DrawingColor.Black);
            graphics.InterpolationMode = DrawingInterpolationMode.HighQualityBicubic;
            graphics.TextRenderingHint = DrawingTextRenderingHint.AntiAliasGridFit;
            graphics.DrawImage(source, 0, 0, width, height);

            foreach (var imageOverlay in imageOverlays)
            {
                if (string.IsNullOrWhiteSpace(imageOverlay.Path) || !File.Exists(imageOverlay.Path))
                {
                    continue;
                }

                using var inset = new DrawingBitmap(imageOverlay.Path);
                var rect = new DrawingRectangleF(
                    (float)Math.Max(0.0, imageOverlay.X),
                    (float)Math.Max(0.0, imageOverlay.Y),
                    (float)Math.Max(24.0, imageOverlay.Width),
                    (float)Math.Max(24.0, imageOverlay.Height));
                graphics.DrawImage(inset, rect);
                using var borderPen = new System.Drawing.Pen(DrawingColor.FromArgb(230, 245, 247, 255), 2f);
                graphics.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);
            }

            foreach (var overlay in overlays)
            {
                if (string.IsNullOrWhiteSpace(overlay.Text))
                {
                    continue;
                }

                using var brush = new DrawingBrush(DrawingColor.FromArgb(overlay.Red, overlay.Green, overlay.Blue));
                using var font = CreateFont(overlay.FontFamilyName, (float)overlay.FontSize);
                using var format = new DrawingStringFormat
                {
                    Alignment = DrawingStringAlignment.Near,
                };
                var x = (float)Math.Max(0.0, overlay.X);
                var y = (float)Math.Max(0.0, overlay.Y);
                var rect = new DrawingRectangleF(x, y, Math.Max(80f, width - x), Math.Max(40f, height - y));
                graphics.DrawString(overlay.Text, font, brush, rect, format);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputImagePath)!);
        composed.Save(outputImagePath, DrawingImageFormat.Png);

        var rgb24 = new byte[width * height * 3];
        var rectLock = new DrawingRectangle(0, 0, width, height);
        var data = composed.LockBits(rectLock, DrawingImageLockMode.ReadOnly, DrawingPixelFormat.Format24bppRgb);
        try
        {
            for (var y = 0; y < height; y++)
            {
                var row = data.Scan0 + (y * data.Stride);
                var bgr = new byte[width * 3];
                Marshal.Copy(row, bgr, 0, bgr.Length);
                for (var x = 0; x < width; x++)
                {
                    var src = x * 3;
                    var dst = ((y * width) + x) * 3;
                    rgb24[dst] = bgr[src + 2];
                    rgb24[dst + 1] = bgr[src + 1];
                    rgb24[dst + 2] = bgr[src];
                }
            }
        }
        finally
        {
            composed.UnlockBits(data);
        }

        return rgb24;
    }

    public static void WriteWaveFile(string path, Pcm16AudioClip clip)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataLength = clip.PcmBytes.Length;
        var byteRate = clip.SampleRate * clip.Channels * 2;
        var blockAlign = (short)(clip.Channels * 2);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)clip.Channels);
        writer.Write(clip.SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        writer.Write(clip.PcmBytes);
    }

    [SupportedOSPlatform("windows")]
    public static void CreateStarterImage(string path, string topHex, string bottomHex, string title, string subtitle)
    {
        if (File.Exists(path))
        {
            return;
        }

        try
        {
            using var bitmap = new DrawingBitmap(320, 256, DrawingPixelFormat.Format24bppRgb);
            using var graphics = DrawingGraphics.FromImage(bitmap);
            using var background = new DrawingGradientBrush(
                new DrawingRectangle(0, 0, 320, 256),
                System.Drawing.ColorTranslator.FromHtml(topHex),
                System.Drawing.ColorTranslator.FromHtml(bottomHex),
                90f);
            graphics.FillRectangle(background, 0, 0, 320, 256);
            using var framePen = new System.Drawing.Pen(DrawingColor.FromArgb(180, 245, 247, 255), 2f);
            graphics.DrawRectangle(framePen, 10, 10, 300, 236);
            graphics.TextRenderingHint = DrawingTextRenderingHint.AntiAliasGridFit;
            using var titleFont = new DrawingFont("Segoe UI", 22, DrawingFontStyle.Bold);
            using var subtitleFont = new DrawingFont("Segoe UI", 13, DrawingFontStyle.Regular);
            using var titleBrush = new DrawingBrush(DrawingColor.FromArgb(245, 255, 255, 255));
            using var subtitleBrush = new DrawingBrush(DrawingColor.FromArgb(215, 196, 200, 216));
            using var centered = new DrawingStringFormat
            {
                Alignment = DrawingStringAlignment.Center,
                LineAlignment = DrawingStringAlignment.Center,
            };
            graphics.DrawString(title, titleFont, titleBrush, new DrawingRectangleF(20, 78, 280, 44), centered);
            graphics.DrawString(subtitle, subtitleFont, subtitleBrush, new DrawingRectangleF(20, 128, 280, 34), centered);
            bitmap.Save(path, DrawingImageFormat.Png);
        }
        catch
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static DrawingFont CreateFont(string fontFamilyName, float fontSize)
    {
        try
        {
            return new DrawingFont(fontFamilyName, fontSize, DrawingFontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        }
        catch
        {
            return new DrawingFont("Segoe UI", fontSize, DrawingFontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        }
    }
}
