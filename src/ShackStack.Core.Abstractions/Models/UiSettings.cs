namespace ShackStack.Core.Abstractions.Models;

public sealed record UiSettings(
    string Theme,
    int WindowWidth,
    int WindowHeight,
    string WaterfallColormap,
    bool BandConditionsEnabled,
    bool ShowExperimentalCw,
    int WaterfallFloorPercent = 8,
    int WaterfallCeilingPercent = 92
);
