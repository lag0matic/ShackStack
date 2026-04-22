namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvest landing zone for the repeater helper fields living inside CSSTVDEM.
/// We are not wiring repeater behavior yet, but preserving the state shape now
/// makes the later port much less error-prone.
/// </summary>
internal sealed class MmsstvRepeaterState
{
    public int Enabled { get; set; }
    public int Squelch { get; set; }
    public int Tone { get; set; }
    public int Mode { get; set; }
    public int Time { get; set; }
    public int Count { get; set; }
    public int Signal { get; set; }
    public int Answer { get; set; }
    public int Relay { get; set; }
    public int Receive { get; set; }
    public int Transmit { get; set; }
    public int ReceiveSignalLevel { get; set; }

    public void Reset()
    {
        Enabled = 0;
        Squelch = 0;
        Tone = 0;
        Mode = 0;
        Time = 0;
        Count = 0;
        Signal = 0;
        Answer = 0;
        Relay = 0;
        Receive = 0;
        Transmit = 0;
        ReceiveSignalLevel = 0;
    }
}
