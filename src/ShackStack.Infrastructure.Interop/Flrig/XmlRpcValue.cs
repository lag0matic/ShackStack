namespace ShackStack.Infrastructure.Interop.Flrig;

public sealed record XmlRpcValue(string Type, string RawValue)
{
    public int AsInt32()
    {
        var scalar = ExtractScalarValue();
        return int.TryParse(scalar, out var value) ? value : 0;
    }

    public double AsDouble()
    {
        var scalar = ExtractScalarValue();
        return double.TryParse(scalar, out var value) ? value : 0d;
    }

    public string AsString() => ExtractScalarValue();

    private string ExtractScalarValue()
    {
        if (!RawValue.Contains('<'))
        {
            return RawValue;
        }

        try
        {
            var wrapped = System.Xml.Linq.XElement.Parse(RawValue);
            return wrapped.Value;
        }
        catch
        {
            return RawValue;
        }
    }
}
