using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IWaterfallRenderSource
{
    WaterfallRenderFrame? GetLatestFrame();
}
