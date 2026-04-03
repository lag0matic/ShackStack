namespace ShackStack.Infrastructure.Radio.Civ;

public enum CivFrameKind
{
    Unknown,
    Acknowledge,
    NegativeAcknowledge,
    SolicitedResponse,
    UnsolicitedEvent,
    StreamData,
}
