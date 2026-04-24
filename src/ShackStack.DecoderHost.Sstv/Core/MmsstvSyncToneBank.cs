namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested landing zone for the CIIRTANK sync/FSK tone filters retuned by
/// CSSTVDEM::InitTone. This keeps the source's tone centers explicit even
/// before the full tank-filter path is threaded into the live receiver.
/// </summary>
internal sealed class MmsstvSyncToneBank
{
    private const double FskSpaceHz = 2100.0;

    public double Tone1080Hz { get; private set; }
    public double Tone1200Hz { get; private set; }
    public double Tone1320Hz { get; private set; }
    public double Tone1900Hz { get; private set; }
    public double ToneFskHz { get; private set; }
    public int AfcFrequencyOffsetHz { get; private set; } = int.MinValue;
    public double ToneOffsetHz { get; private set; } = double.NaN;

    public void InitTone(int deltaFrequencyHz, double toneOffsetHz = 0.0)
    {
        if (AfcFrequencyOffsetHz == deltaFrequencyHz && ToneOffsetHz.Equals(toneOffsetHz))
        {
            return;
        }

        Tone1080Hz = 1080.0 + deltaFrequencyHz + toneOffsetHz;
        Tone1200Hz = 1200.0 + deltaFrequencyHz + toneOffsetHz;
        Tone1320Hz = 1320.0 + deltaFrequencyHz + toneOffsetHz;
        Tone1900Hz = 1900.0 + deltaFrequencyHz + toneOffsetHz;
        ToneFskHz = FskSpaceHz + deltaFrequencyHz + toneOffsetHz;
        AfcFrequencyOffsetHz = deltaFrequencyHz;
        ToneOffsetHz = toneOffsetHz;
    }
}
