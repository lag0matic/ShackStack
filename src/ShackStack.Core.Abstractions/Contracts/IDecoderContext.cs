using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IDecoderContext
{
    IObservable<AudioBuffer> SampleStream { get; }
}
