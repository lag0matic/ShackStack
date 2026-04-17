namespace ShackStack.DecoderHost.GplWsjtx.Ft4;

internal static class Ft4Constants
{
    public const int InformationBits = 91;
    public const int DataSymbols = 87;
    public const int SyncSymbols = 16;
    public const int ChannelSymbols = SyncSymbols + DataSymbols;
    public const int TotalSymbols = ChannelSymbols + 2;
    public const int InputSampleRate = 12_000;
    public const double CycleSeconds = 7.5;
    public const int InputSamplesPerCycle = 90_000;
    public const int SamplesPerSymbol = 576;
    public const int InputFrameSamples = 21 * 3_456;
    public const int SymbolFftLength = 2_304;
    public const int SymbolFftBins = SymbolFftLength / 2;
    public const int StepSamples = SamplesPerSymbol;
    public const int HalfSymbolSteps = (InputFrameSamples - SymbolFftLength) / StepSamples;
    public const int DownsampleFactor = 18;
    public const int DownsampledSampleRate = InputSampleRate / DownsampleFactor;
    public const int DownsampledLength = InputFrameSamples / DownsampleFactor;
    public const int DownsampledSamplesPerSymbol = SamplesPerSymbol / DownsampleFactor;
    public const double Baud = (double)InputSampleRate / SamplesPerSymbol;
}
