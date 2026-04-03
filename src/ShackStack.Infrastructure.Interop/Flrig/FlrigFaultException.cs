namespace ShackStack.Infrastructure.Interop.Flrig;

public sealed class FlrigFaultException(int code, string message) : Exception(message)
{
    public XmlRpcFault Fault { get; } = new(code, message);
}
