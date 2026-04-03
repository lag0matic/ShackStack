namespace ShackStack.Infrastructure.Radio.Icom;

internal static class IcomCivConstants
{
    public const byte ControllerAddress = 0xE0;

    public const byte GetFrequency = 0x03;
    public const byte GetMode = 0x04;
    public const byte SetFrequency = 0x05;
    public const byte SetMode = 0x06;
    public const byte VfoCommand = 0x07;
    public const byte SplitCommand = 0x0F;
    public const byte ReadMeter = 0x15;
    public const byte CwMessageContents = 0x17;
    public const byte SetControlState = 0x1C;
    public const byte VfoMode = 0x26;
    public const byte ScopeCommand = 0x27;
    public const byte ControlLevelCommand = 0x14;
    public const byte ControlFunctionCommand = 0x16;
    public const byte SetReadTranceiveCommand = 0x1A;

    public const byte PttSubcommand = 0x00;
    public const byte SmeterSubcommand = 0x02;
    public const byte CwPitchLevelSubcommand = 0x09;
    public const byte RfPowerLevelSubcommand = 0x0A;
    public const byte MicGainLevelSubcommand = 0x0B;
    public const byte KeySpeedLevelSubcommand = 0x0C;
    public const byte ManualNotchPositionLevelSubcommand = 0x0D;
    public const byte CompressorLevelSubcommand = 0x0E;
    public const byte NoiseReductionLevelSubcommand = 0x06;
    public const byte AutoNotchFunctionSubcommand = 0x41;
    public const byte CompressorFunctionSubcommand = 0x44;
    public const byte ManualNotchFunctionSubcommand = 0x48;
    public const byte NoiseBlankerFunctionSubcommand = 0x22;
    public const byte NoiseReductionFunctionSubcommand = 0x40;
    public const byte FilterShapeFunctionSubcommand = 0x56;
    public const byte ManualNotchWidthFunctionSubcommand = 0x57;
    public const byte IpPlusFunctionSubcommand = 0x65;
    public const byte ScopeDataSubcommand = 0x00;
    public const byte ScopeEnableSubcommand = 0x10;
    public const byte ScopeOutputEnableSubcommand = 0x11;
    public const byte ScopeModeSubcommand = 0x14;
    public const byte ScopeSpanSubcommand = 0x15;
    public const byte ScopeEdgeSubcommand = 0x16;
    public const byte ScopeHoldSubcommand = 0x17;
    public const byte ScopeRefSubcommand = 0x19;
    public const byte ScopeSpeedSubcommand = 0x1A;
    public const byte TranceiveSetReadSubcommand = 0x05;
    public const byte VfoASubcommand = 0x00;
    public const byte VfoBSubcommand = 0x01;
    public const byte VfoEqualizeSubcommand = 0xA0;
    public const byte VfoExchangeSubcommand = 0xB0;
}
