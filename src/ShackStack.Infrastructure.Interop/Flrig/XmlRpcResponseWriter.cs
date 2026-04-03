using System.Security;
using System.Text;

namespace ShackStack.Infrastructure.Interop.Flrig;

public static class XmlRpcResponseWriter
{
    public static byte[] WriteValue(object? value)
    {
        var inner = WriteInnerValue(value);
        var xml =
            "<?xml version=\"1.0\"?>\n" +
            "<methodResponse><params><param>\n" +
            $"        {inner}\n" +
            "</param></params></methodResponse>\n";
        return Encoding.UTF8.GetBytes(xml);
    }

    public static byte[] WriteFault(XmlRpcFault fault)
    {
        var xml =
            "<?xml version=\"1.0\"?>\n" +
            "<methodResponse><fault><value><struct>" +
            $"<member><name>faultCode</name><value><int>{fault.Code}</int></value></member>" +
            $"<member><name>faultString</name><value>{Escape(fault.Message)}</value></member>" +
            "</struct></value></fault></methodResponse>\n";
        return Encoding.UTF8.GetBytes(xml);
    }

    private static string WriteInnerValue(object? value) => value switch
    {
        null => "<value></value>",
        int i => $"<value><i4>{i}</i4></value>",
        double d => $"<value><double>{d}</double></value>",
        bool b => $"<value><boolean>{(b ? 1 : 0)}</boolean></value>",
        string s => $"<value>{Escape(s)}</value>",
        IEnumerable<object?> seq => $"<value><array><data>{string.Concat(seq.Select(WriteInnerValue))}</data></array></value>",
        _ => $"<value>{Escape(value.ToString() ?? string.Empty)}</value>",
    };

    private static string Escape(string text) => SecurityElement.Escape(text) ?? string.Empty;
}
