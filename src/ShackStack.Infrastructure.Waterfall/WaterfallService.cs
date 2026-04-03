using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Waterfall;

public sealed class WaterfallService : IWaterfallService, IWaterfallRenderSource
{
    private const int DefaultHeight = 160;

    private readonly SimpleSubject<SpectrumFrame> _spectrum = new();
    private readonly SimpleSubject<WaterfallRow> _waterfall = new();
    private readonly object _sync = new();
    private WaterfallRenderFrame? _latestFrame;
    private int _width;
    private int _height = DefaultHeight;
    private float[][]? _history;
    private long _centerFrequencyHz;
    private int _spanHz;
    private float _floor = 0.08f;
    private float _ceiling = 0.92f;
    private int _zoom = 1;

    public IObservable<SpectrumFrame> SpectrumStream => _spectrum;

    public IObservable<WaterfallRow> WaterfallStream => _waterfall;

    public void PushSamples(ReadOnlyMemory<float> samples)
    {
    }

    public void UpdateDisplaySettings(float floor, float ceiling, int zoom)
    {
        lock (_sync)
        {
            _floor = Math.Clamp(floor, 0f, 0.98f);
            _ceiling = Math.Clamp(ceiling, _floor + 0.01f, 1f);
            _zoom = Math.Clamp(zoom, 1, 10);
            PublishLatestFrame();
        }
    }

    public void PushScopeRow(WaterfallRow row)
    {
        if (row.Bins.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            EnsureBuffers(row.Bins.Length);
            ShiftRowsDown();
            _history![0] = (float[])row.Bins.Clone();
            _centerFrequencyHz = row.CenterFrequencyHz;
            _spanHz = row.SpanHz;
            PublishLatestFrame();
            _waterfall.OnNext(row);
        }
    }

    public WaterfallRenderFrame? GetLatestFrame() => _latestFrame;

    private void EnsureBuffers(int width)
    {
        if (_history is not null && _width == width)
        {
            return;
        }

        _width = width;
        _history = new float[_height][];
        for (var i = 0; i < _height; i++)
        {
            _history[i] = new float[_width];
        }
    }

    private void ShiftRowsDown()
    {
        if (_history is null)
        {
            return;
        }

        for (var i = _height - 1; i > 0; i--)
        {
            _history[i] = _history[i - 1];
        }

        _history[0] = new float[_width];
    }

    private void PublishLatestFrame()
    {
        if (_history is null || _width == 0 || _spanHz <= 0)
        {
            return;
        }

        var visibleBins = BuildVisibleSpectrum();
        var pixels = BuildWaterfallPixels();
        var visibleSpanHz = Math.Max(1, _spanHz / Math.Max(1, _zoom));
        var spectrum = new SpectrumFrame(visibleBins, _centerFrequencyHz, visibleSpanHz);
        _latestFrame = new WaterfallRenderFrame(
            _width,
            _height,
            _centerFrequencyHz,
            visibleSpanHz,
            visibleBins,
            pixels);
        _spectrum.OnNext(spectrum);
    }

    private float[] BuildVisibleSpectrum()
    {
        var result = new float[_width];
        var newest = _history![0];
        for (var x = 0; x < _width; x++)
        {
            var sourceIndex = MapSourceIndex(x);
            result[x] = Normalize(newest[sourceIndex]);
        }

        return result;
    }

    private byte[] BuildWaterfallPixels()
    {
        var pixels = new byte[_width * _height * 4];
        for (var y = 0; y < _height; y++)
        {
            var row = _history![y];
            for (var x = 0; x < _width; x++)
            {
                var sourceIndex = MapSourceIndex(x);
                var value = Normalize(row[sourceIndex]);
                var color = MapWaterfallColor(value);
                var offset = ((y * _width) + x) * 4;
                pixels[offset + 0] = color.b;
                pixels[offset + 1] = color.g;
                pixels[offset + 2] = color.r;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private int MapSourceIndex(int outputIndex)
    {
        if (_width <= 1)
        {
            return 0;
        }

        var visibleWidth = Math.Max(4, _width / Math.Max(1, _zoom));
        var start = Math.Max(0, (_width - visibleWidth) / 2);
        var t = outputIndex / (float)(_width - 1);
        var index = start + (int)MathF.Round(t * (visibleWidth - 1));
        return Math.Clamp(index, 0, _width - 1);
    }

    private float Normalize(float value)
    {
        var range = Math.Max(0.01f, _ceiling - _floor);
        return Math.Clamp((value - _floor) / range, 0f, 1f);
    }

    private static (byte r, byte g, byte b) MapWaterfallColor(float value)
    {
        value = Math.Clamp(value, 0f, 1f);

        if (value < 0.20f)
        {
            var t = value / 0.20f;
            return Blend((0, 0, 0), (0, 18, 48), t);
        }

        if (value < 0.45f)
        {
            var t = (value - 0.20f) / 0.25f;
            return Blend((0, 18, 48), (0, 120, 255), t);
        }

        if (value < 0.70f)
        {
            var t = (value - 0.45f) / 0.25f;
            return Blend((0, 120, 255), (0, 255, 190), t);
        }

        if (value < 0.88f)
        {
            var t = (value - 0.70f) / 0.18f;
            return Blend((0, 255, 190), (255, 235, 70), t);
        }

        var finalT = (value - 0.88f) / 0.12f;
        return Blend((255, 235, 70), (255, 255, 255), finalT);
    }

    private static (byte r, byte g, byte b) Blend((int r, int g, int b) start, (int r, int g, int b) end, float t)
    {
        var clamped = Math.Clamp(t, 0f, 1f);
        return
        (
            (byte)Math.Clamp(start.r + ((end.r - start.r) * clamped), 0f, 255f),
            (byte)Math.Clamp(start.g + ((end.g - start.g) * clamped), 0f, 255f),
            (byte)Math.Clamp(start.b + ((end.b - start.b) * clamped), 0f, 255f)
        );
    }
}
