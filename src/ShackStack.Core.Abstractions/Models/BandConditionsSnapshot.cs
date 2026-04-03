namespace ShackStack.Core.Abstractions.Models;

public sealed record BandConditionsSnapshot(
    string Updated,
    string SolarFlux,
    string Sunspots,
    string AIndex,
    string KIndex,
    string XRay,
    string GeomagneticField,
    IReadOnlyList<BandConditionCell> Bands
);
