using System.Xml.Linq;

namespace ShackStack.Infrastructure.Interop.Flrig;

public static class MulticallParser
{
    public static IReadOnlyList<XmlRpcRequest> Parse(string rawValueInnerXml)
    {
        var wrapped = $"<root>{rawValueInnerXml}</root>";
        var doc = XDocument.Parse(wrapped);
        var requests = new List<XmlRpcRequest>();

        foreach (var structElement in doc.Descendants("struct"))
        {
            var methodName = structElement
                .Elements("member")
                .FirstOrDefault(x => x.Element("name")?.Value == "methodName")
                ?.Element("value")
                ?.Value
                ?.Trim();

            var paramsArray = structElement
                .Elements("member")
                .FirstOrDefault(x => x.Element("name")?.Value == "params")
                ?.Element("value")
                ?.Descendants("param")
                .Select(p => new XmlRpcValue("string", p.Value))
                .ToArray() ?? [];

            if (!string.IsNullOrWhiteSpace(methodName))
            {
                requests.Add(new XmlRpcRequest(methodName, paramsArray));
            }
        }

        return requests;
    }
}
