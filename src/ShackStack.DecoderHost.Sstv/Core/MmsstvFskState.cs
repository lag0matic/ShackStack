namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvest landing zone for the FSK-related working fields embedded in CSSTVDEM.
/// This is intentionally plain state so we can port the original control flow
/// before deciding what should be higher-level later.
/// </summary>
internal sealed class MmsstvFskState
{
    public const int DataCapacity = 20;

    public int Decode { get; set; }
    public int Receive { get; set; }
    public int Mode { get; set; }
    public int Time { get; set; }
    public int Count { get; set; }
    public int BitCount { get; set; }
    public int NextIndex { get; set; }
    public double NextDelta { get; set; }
    public byte Shift { get; set; }
    public byte Carry { get; set; }
    public byte[] Data { get; } = new byte[DataCapacity];
    public char[] Call { get; } = new char[DataCapacity];
    public int NumberReceive { get; set; }
    public int Number { get; set; }
    public char[] NumberText { get; } = new char[DataCapacity];

    public void Reset()
    {
        Decode = 0;
        Receive = 0;
        Mode = 0;
        Time = 0;
        Count = 0;
        BitCount = 0;
        NextIndex = 0;
        NextDelta = 0.0;
        Shift = 0;
        Carry = 0;
        NumberReceive = 0;
        Number = 0;
        Array.Clear(Data);
        Array.Clear(Call);
        Array.Clear(NumberText);
    }
}
