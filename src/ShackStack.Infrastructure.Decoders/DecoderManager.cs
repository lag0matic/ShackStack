using ShackStack.Core.Abstractions.Contracts;

namespace ShackStack.Infrastructure.Decoders;

public sealed class DecoderManager(IEnumerable<IDecoderPlugin> plugins)
{
    public IReadOnlyList<IDecoderPlugin> Plugins { get; } = plugins.ToList();
}
