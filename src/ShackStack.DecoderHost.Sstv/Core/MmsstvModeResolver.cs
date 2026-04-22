namespace ShackStack.DecoderHost.Sstv.Core;

internal static class MmsstvModeResolver
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Martin M1"] = "Martin 1",
            ["Martin M2"] = "Martin 2",
            ["Scottie DX"] = "Scottie DX",
            ["PD 50"] = "PD 50",
            ["PD 90"] = "PD 90",
            ["PD 120"] = "PD 120",
            ["PD 160"] = "PD 160",
            ["PD 180"] = "PD 180",
            ["PD 240"] = "PD 240",
            ["PD 290"] = "PD 290",
            ["Scottie 1"] = "Scottie 1",
            ["Scottie 2"] = "Scottie 2",
            ["Robot 36"] = "Robot 36",
            ["Auto Detect"] = "Auto Detect",
        };

    public static string NormalizeName(string? modeName)
    {
        if (string.IsNullOrWhiteSpace(modeName))
        {
            return "Auto Detect";
        }

        var trimmed = modeName.Trim();
        return Aliases.TryGetValue(trimmed, out var canonical)
            ? canonical
            : trimmed;
    }
}
