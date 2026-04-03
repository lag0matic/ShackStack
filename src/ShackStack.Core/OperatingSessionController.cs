using ShackStack.Core.Abstractions.Contracts;

namespace ShackStack.Core;

public sealed class OperatingSessionController(
    IRadioService radioService,
    IAudioService audioService,
    IWaterfallService waterfallService,
    IInteropService interopService)
{
    public IRadioService RadioService { get; } = radioService;
    public IAudioService AudioService { get; } = audioService;
    public IWaterfallService WaterfallService { get; } = waterfallService;
    public IInteropService InteropService { get; } = interopService;
}
