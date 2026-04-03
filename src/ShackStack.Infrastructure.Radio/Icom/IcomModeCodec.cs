using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Infrastructure.Radio.Icom;

internal static class IcomModeCodec
{
    public static RadioMode Decode(byte value, byte dataMode = 0x00) => (value, dataMode) switch
    {
        (0x00, 0x01) => RadioMode.LsbData,
        (0x00, _) => RadioMode.Lsb,
        (0x01, 0x01) => RadioMode.UsbData,
        (0x01, _) => RadioMode.Usb,
        (0x02, _) => RadioMode.Am,
        (0x03, _) => RadioMode.Cw,
        (0x04, _) => RadioMode.Rtty,
        (0x05, _) => RadioMode.Fm,
        _ => RadioMode.Usb,
    };

    public static byte Encode(RadioMode mode) => mode switch
    {
        RadioMode.Lsb => 0x00,
        RadioMode.LsbData => 0x00,
        RadioMode.Usb => 0x01,
        RadioMode.UsbData => 0x01,
        RadioMode.Am => 0x02,
        RadioMode.Cw => 0x03,
        RadioMode.Rtty => 0x04,
        RadioMode.Fm => 0x05,
        _ => 0x01,
    };

    public static byte EncodeDataMode(RadioMode mode) => mode switch
    {
        RadioMode.LsbData => 0x01,
        RadioMode.UsbData => 0x01,
        _ => 0x00,
    };

    public static int DecodeFilterWidth(RadioMode mode, byte filterSlot) => mode switch
    {
        RadioMode.Cw => filterSlot switch
        {
            0x01 => 250,
            0x02 => 500,
            0x03 => 1200,
            _ => 500,
        },
        RadioMode.Rtty => filterSlot switch
        {
            0x01 => 250,
            0x02 => 500,
            0x03 => 1200,
            _ => 500,
        },
        RadioMode.LsbData => filterSlot switch
        {
            0x01 => 1800,
            0x02 => 2400,
            0x03 => 3000,
            _ => 2400,
        },
        RadioMode.UsbData => filterSlot switch
        {
            0x01 => 1800,
            0x02 => 2400,
            0x03 => 3000,
            _ => 2400,
        },
        _ => filterSlot switch
        {
            0x01 => 1800,
            0x02 => 2400,
            0x03 => 3000,
            _ => 2400,
        },
    };

    public static byte EncodeFilterSlot(RadioMode mode, int widthHz) => mode switch
    {
        RadioMode.Cw => widthHz switch
        {
            <= 250 => 0x01,
            <= 500 => 0x02,
            _ => 0x03,
        },
        RadioMode.Rtty => widthHz switch
        {
            <= 250 => 0x01,
            <= 500 => 0x02,
            _ => 0x03,
        },
        RadioMode.LsbData => widthHz switch
        {
            <= 1800 => 0x01,
            <= 2400 => 0x02,
            _ => 0x03,
        },
        RadioMode.UsbData => widthHz switch
        {
            <= 1800 => 0x01,
            <= 2400 => 0x02,
            _ => 0x03,
        },
        _ => widthHz switch
        {
            <= 1800 => 0x01,
            <= 2400 => 0x02,
            _ => 0x03,
        },
    };

    public static IReadOnlyList<int> GetFilterOptions(RadioMode mode) => mode switch
    {
        RadioMode.Cw => [250, 500, 1200],
        RadioMode.Rtty => [250, 500, 1200],
        RadioMode.LsbData => [1800, 2400, 3000],
        RadioMode.UsbData => [1800, 2400, 3000],
        _ => [1800, 2400, 3000],
    };
}
