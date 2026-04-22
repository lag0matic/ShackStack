namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested from MMSSTV's CSmooz ring-buffer average helper.
/// </summary>
internal sealed class MmsstvSmoother
{
    private double[] _buffer = [0.0];
    private int _writeIndex;

    public int Capacity => _buffer.Length;
    public int Count { get; private set; }

    public double SetData(double value)
    {
        for (var i = 0; i < _buffer.Length; i++)
        {
            _buffer[i] = value;
        }

        _writeIndex = 0;
        Count = _buffer.Length;
        return value;
    }

    public double Average()
    {
        var sum = 0.0;
        for (var i = 0; i < Count; i++)
        {
            sum += _buffer[i];
        }

        return Count > 0 ? sum / Count : 0.0;
    }

    public void SetCount(double count)
    {
        var size = Math.Max(1, (int)Math.Round(count));
        if (_buffer.Length == size)
        {
            return;
        }

        _buffer = new double[size];
        _writeIndex = 0;
        Count = 0;
    }

    public double Average(double value)
    {
        _buffer[_writeIndex] = value;
        _writeIndex++;
        if (_writeIndex >= _buffer.Length)
        {
            _writeIndex = 0;
        }

        if (Count < _buffer.Length)
        {
            Count++;
        }

        return Average();
    }
}
