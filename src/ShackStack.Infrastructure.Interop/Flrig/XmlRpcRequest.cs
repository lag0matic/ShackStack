namespace ShackStack.Infrastructure.Interop.Flrig;

public sealed record XmlRpcRequest(
    string MethodName,
    IReadOnlyList<XmlRpcValue> Parameters
);
