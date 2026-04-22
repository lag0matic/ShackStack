namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-shaped port of MMSSTV's CIIRTANK narrow band filter.
/// </summary>
internal sealed class MmsstvIirTank
{
    private double _z1;
    private double _z2;

    public double A0 { get; private set; }
    public double B1 { get; private set; }
    public double B2 { get; private set; }

    public MmsstvIirTank(double sampleRate)
    {
        SetFrequency(2000.0, sampleRate, 50.0);
    }

    public void SetFrequency(double frequencyHz, double sampleRate, double bandwidthHz)
    {
        var lb1 = 2.0 * Math.Exp(-Math.PI * bandwidthHz / sampleRate) * Math.Cos(2.0 * Math.PI * frequencyHz / sampleRate);
        var lb2 = -Math.Exp(-2.0 * Math.PI * bandwidthHz / sampleRate);
        double la0;
        if (bandwidthHz != 0.0)
        {
            la0 = Math.Sin(2.0 * Math.PI * frequencyHz / sampleRate) / ((sampleRate / 6.0) / bandwidthHz);
        }
        else
        {
            la0 = Math.Sin(2.0 * Math.PI * frequencyHz / sampleRate);
        }

        B1 = lb1;
        B2 = lb2;
        A0 = la0;
    }

    public double Process(double sample)
    {
        var output = sample * A0;
        output += _z1 * B1;
        output += _z2 * B2;
        _z2 = _z1;
        if (Math.Abs(output) < 1e-37)
        {
            output = 0.0;
        }

        _z1 = output;
        return output;
    }

    public void Clear()
    {
        _z1 = 0.0;
        _z2 = 0.0;
    }
}
