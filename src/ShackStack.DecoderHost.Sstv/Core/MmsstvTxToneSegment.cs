namespace ShackStack.DecoderHost.Sstv.Core;

internal readonly record struct MmsstvTxToneSegment(
    double FrequencyHz,
    double DurationMs,
    int Marker = 0)
{
    public bool IsSilence => FrequencyHz <= 0.0;
}
