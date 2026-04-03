using System.Xml.Linq;

namespace ShackStack.Infrastructure.Interop.Flrig;

public static class XmlRpcParser
{
    public static XmlRpcRequest Parse(string xml)
    {
        var doc = XDocument.Parse(xml.Trim());
        var methodName = doc.Root?.Element("methodName")?.Value?.Trim()
            ?? throw new InvalidOperationException("XML-RPC request missing methodName.");

        var parameters = doc.Root?
            .Element("params")?
            .Elements("param")
            .Select(ParseParam)
            .ToArray()
            ?? [];

        return new XmlRpcRequest(methodName, parameters);
    }

    private static XmlRpcValue ParseParam(XElement param)
    {
        var valueElement = param.Element("value")
            ?? throw new InvalidOperationException("XML-RPC param missing value.");

        if (!valueElement.Elements().Any())
        {
            return new XmlRpcValue("string", valueElement.Value);
        }

        var typed = valueElement.Elements().First();
        return new XmlRpcValue(typed.Name.LocalName, typed.ToString(SaveOptions.DisableFormatting));
    }
}
