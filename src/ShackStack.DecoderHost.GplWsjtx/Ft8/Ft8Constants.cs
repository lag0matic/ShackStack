namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal static class Ft8Constants
{
    public const int InformationBits = 91;
    public const int DataSymbols = 58;
    public const int SyncSymbols = 21;
    public const int ChannelSymbols = SyncSymbols + DataSymbols;
    public const int InputSampleRate = 12_000;
    public const int CycleSeconds = 15;
    public const int InputSamplesPerCycle = InputSampleRate * CycleSeconds;
    public const int SamplesPerSymbol = 1_920;
    public const int StepSamples = SamplesPerSymbol / 4;
    public const int SymbolFftLength = 2 * SamplesPerSymbol;
    public const int SymbolFftBins = SymbolFftLength / 2;
    public const int HalfSymbolSteps = InputSamplesPerCycle / StepSamples - 3;
    public const int DownsampleFactor = 60;
    public const int DownsampledSampleRate = InputSampleRate / DownsampleFactor;
    public const int DownsampledFftLength = 3_200;
    public const int LongFftLength = 192_000;
    public const int UsefulDownsampledLength = 2_812;
    public const double Baud = (double)InputSampleRate / SamplesPerSymbol;
}
