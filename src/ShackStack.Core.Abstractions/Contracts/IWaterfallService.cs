using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IWaterfallService
{
    void PushSamples(ReadOnlyMemory<float> samples);
    void PushScopeRow(WaterfallRow row);
    void UpdateDisplaySettings(float floor, float ceiling, int zoom);
    IObservable<SpectrumFrame> SpectrumStream { get; }
    IObservable<WaterfallRow> WaterfallStream { get; }
}
