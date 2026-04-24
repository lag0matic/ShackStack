namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested landing zone for the geometry portions of CSSTVSET::SetSampFreq.
/// Keeps the important per-mode receive layout values in one place so the
/// receiver, timing engine, and harness all follow the same source-shaped math.
/// </summary>
internal sealed record MmsstvPictureGeometry(
    int LineSamples,
    int SyncSamples,
    int OffsetSamples,
    int OffsetPreviewSamples,
    int ScanSamples,
    int ScanSamplesAdjusted,
    int Scan2Samples,
    int Scan2SamplesAdjusted,
    int GreenStartSamples,
    int BlueStartSamples,
    int RedStartSamples,
    int GapSamples,
    int SyncStartSamples,
    int ChromaSelectStartSamples,
    int ChromaGreenEndSamples,
    int ChromaBlueEndSamples,
    double DrawLineSamples,
    double DrawOffsetSamples,
    double DrawOffsetPreviewSamples,
    double DrawScanSamples,
    double DrawScan2Samples,
    double DrawScanSamplesAdjusted,
    double DrawScan2SamplesAdjusted,
    double DrawChromaGreenStartSamples,
    double DrawChromaGreenEndSamples,
    double DrawChromaBlueStartSamples,
    double DrawChromaBlueEndSamples,
    int BlackAdjustSamples)
{
    public static MmsstvPictureGeometry Create(SstvModeProfile profile, int sampleRate)
    {
        var source = MmsstvSetSampFreq(profile.Id, sampleRate);
        return new MmsstvPictureGeometry(
            TruncatedSamples(source.TimingMs, sampleRate),
            SyncSamples: MapSyncSamples(profile, source, sampleRate),
            OffsetSamples: Samples(source.OffsetMs, sampleRate),
            OffsetPreviewSamples: Samples(source.OffsetPreviewMs, sampleRate),
            ScanSamples: Samples(source.ScanMs, sampleRate),
            ScanSamplesAdjusted: PositiveSamples(source.ScanAdjustedMs, sampleRate),
            Scan2Samples: Samples(source.Scan2Ms, sampleRate),
            Scan2SamplesAdjusted: Samples(source.Scan2AdjustedMs, sampleRate),
            GreenStartSamples: MapGreenStartSamples(profile, source, sampleRate),
            BlueStartSamples: MapBlueStartSamples(profile, source, sampleRate),
            RedStartSamples: MapRedStartSamples(profile, source, sampleRate),
            GapSamples: MapGapSamples(profile, source, sampleRate),
            SyncStartSamples: MapSyncStartSamples(profile, source, sampleRate),
            ChromaSelectStartSamples: Samples(source.SyncGapMs, sampleRate),
            ChromaGreenEndSamples: Samples(source.ChromaGreenEndMs, sampleRate),
            ChromaBlueEndSamples: Samples(source.ChromaBlueStartMs, sampleRate),
            DrawLineSamples: SourceSamples(source.TimingMs, sampleRate),
            DrawOffsetSamples: SourceSamples(source.OffsetMs, sampleRate),
            DrawOffsetPreviewSamples: SourceSamples(source.OffsetPreviewMs, sampleRate),
            DrawScanSamples: SourceSamples(source.ScanMs, sampleRate),
            DrawScan2Samples: SourceSamples(source.Scan2Ms, sampleRate),
            DrawScanSamplesAdjusted: Math.Max(1.0, SourceSamples(source.ScanAdjustedMs, sampleRate)),
            DrawScan2SamplesAdjusted: SourceSamples(source.Scan2AdjustedMs, sampleRate),
            DrawChromaGreenStartSamples: SourceSamples(source.SyncGapMs, sampleRate),
            DrawChromaGreenEndSamples: SourceSamples(source.ChromaGreenEndMs, sampleRate),
            DrawChromaBlueStartSamples: SourceSamples(source.ChromaRedStartMs, sampleRate),
            DrawChromaBlueEndSamples: SourceSamples(source.ChromaBlueStartMs, sampleRate),
            BlackAdjustSamples: Math.Max(1, Samples(source.BlackAdjustMs, sampleRate)));
    }

    public static int CalculateLineSamples(SstvModeProfile profile, int sampleRate)
        => TruncatedSamples(MmsstvSetSampFreq(profile.Id, sampleRate).TimingMs, sampleRate);

    private static MmsstvSourceGeometry MmsstvSetSampFreq(SstvModeId id, int sampleRate)
    {
        var source = SourceTiming(id);
        var kss = source.ScanMs;
        var ks2s = source.Scan2Ms;
        var ksbDivisor = 640.0;

        switch (id)
        {
            case SstvModeId.Pd120:
            case SstvModeId.Pd160:
            case SstvModeId.Pd180:
            case SstvModeId.Pd240:
            case SstvModeId.Pd290:
            case SstvModeId.Pasokon3:
            case SstvModeId.Pasokon5:
            case SstvModeId.Pasokon7:
                kss = source.ScanMs - (source.ScanMs / 480.0);
                ks2s = source.Scan2Ms - (source.Scan2Ms / 480.0);
                ksbDivisor = 1280.0;
                break;
            case SstvModeId.WraseMp73:
            case SstvModeId.WraseMn73:
            case SstvModeId.ScottieDx:
                kss = source.ScanMs - (source.ScanMs / 1280.0);
                ks2s = source.Scan2Ms - (source.Scan2Ms / 1280.0);
                ksbDivisor = 1280.0;
                break;
            case SstvModeId.Sc2_180:
            case SstvModeId.WraseMp115:
            case SstvModeId.WraseMp140:
            case SstvModeId.WraseMp175:
            case SstvModeId.WraseMr90:
            case SstvModeId.WraseMr115:
            case SstvModeId.WraseMr140:
            case SstvModeId.WraseMr175:
            case SstvModeId.WraseMl180:
            case SstvModeId.WraseMl240:
            case SstvModeId.WraseMl280:
            case SstvModeId.WraseMl320:
            case SstvModeId.WraseMn110:
            case SstvModeId.WraseMn140:
            case SstvModeId.WraseMc110:
            case SstvModeId.WraseMc140:
            case SstvModeId.WraseMc180:
                kss = source.ScanMs;
                ks2s = source.Scan2Ms;
                ksbDivisor = 1280.0;
                break;
            case SstvModeId.WraseMr73:
                kss = source.ScanMs - (source.ScanMs / 640.0);
                ks2s = source.Scan2Ms - (source.Scan2Ms / 1024.0);
                ksbDivisor = 1024.0;
                break;
            default:
                kss = source.ScanMs - (source.ScanMs / 240.0);
                ks2s = source.Scan2Ms - (source.Scan2Ms / 240.0);
                ksbDivisor = 640.0;
                break;
        }

        return source with
        {
            ScanAdjustedMs = kss,
            Scan2AdjustedMs = ks2s,
            BlackAdjustMs = kss / ksbDivisor
        };
    }

    private static MmsstvSourceGeometry SourceTiming(SstvModeId id)
        => id switch
        {
            SstvModeId.Robot36 => new(150.0, 88.0, 44.0, 12.0, 10.7, 89.25, 91.5, 94.0, 138.0),
            SstvModeId.Robot72 => new(300.0, 138.0, 69.0, 12.0, 10.7, 144.0, 213.0, 219.0, 288.0),
            SstvModeId.Avt90 => new(375.0, 125.0, 0.0, 0.0, 0.0, 125.0, 250.0, 250.0, 375.0),
            SstvModeId.Scottie2 => new(277.692, 88.064, 0.0, 10.5, 10.8, 89.564, 177.628, 179.128, 267.192),
            SstvModeId.ScottieDx => new(1050.3, 345.6, 0.0, 10.5, 10.2, 347.1, 692.7, 694.2, 1039.8),
            SstvModeId.Martin1 => new(446.446, 146.432, 0.0, 5.434, 7.2, 147.004, 293.436, 294.008, 440.44),
            SstvModeId.Martin2 => new(226.798, 73.216, 0.0, 5.434, 7.4, 73.788, 147.004, 147.576, 220.792),
            SstvModeId.Sc2_180 => new(711.0437, 235.0, 0.0, 6.0437, 7.8, 235.0, 470.0, 470.0, 705.0),
            SstvModeId.Sc2_120 => new(475.52248, 156.5, 0.0, 6.02248, 7.5, 156.5, 313.0, 313.0, 469.5),
            SstvModeId.Sc2_60 => new(240.3846, 78.128, 0.0, 6.0006, 7.9, 78.128, 156.256, 156.256, 234.384),
            SstvModeId.Pd50 => new(388.160, 91.520, 0.0, 22.080, 19.300, 91.520, 183.040, 183.040, 274.560),
            SstvModeId.Pd90 => new(703.040, 170.240, 0.0, 22.080, 18.900, 170.240, 340.480, 340.480, 510.720),
            SstvModeId.Pd120 => new(508.480, 121.600, 0.0, 22.080, 19.400, 121.600, 243.200, 243.200, 364.800),
            SstvModeId.Pd160 => new(804.416, 195.584, 0.0, 22.080, 18.900, 195.584, 391.168, 391.168, 586.752),
            SstvModeId.Pd180 => new(754.240, 183.040, 0.0, 22.080, 18.900, 183.040, 366.080, 366.080, 549.120),
            SstvModeId.Pd240 => new(1000.0, 244.480, 0.0, 22.080, 18.900, 244.480, 488.960, 488.960, 733.440),
            SstvModeId.Pd290 => new(937.280, 228.800, 0.0, 22.080, 18.900, 228.800, 457.600, 457.600, 686.400),
            SstvModeId.Pasokon3 => new(409.375, 133.333, 0.0, 6.25, 7.80, 134.375, 267.708, 268.75, 402.083),
            SstvModeId.Pasokon5 => new(614.0625, 200.0, 0.0, 9.375375, 9.20, 201.562375, 401.562375, 403.12475, 603.12475),
            SstvModeId.Pasokon7 => new(818.75, 266.667, 0.0, 12.5, 11.50, 268.75, 535.417, 537.5, 804.167),
            SstvModeId.WraseMr73 => new(286.3, 138.0, 69.0, 10.0, 10.6, 138.1, 207.1, 207.2, 276.2),
            SstvModeId.WraseMr90 => new(352.3, 171.0, 85.5, 10.0, 10.6, 171.1, 256.6, 256.7, 342.2),
            SstvModeId.WraseMr115 => new(450.3, 220.0, 110.0, 10.0, 10.6, 220.1, 330.1, 330.2, 440.2),
            SstvModeId.WraseMr140 => new(548.3, 269.0, 134.5, 10.0, 10.6, 269.1, 403.6, 403.7, 538.2),
            SstvModeId.WraseMr175 => new(684.3, 337.0, 168.5, 10.0, 10.6, 337.1, 505.6, 505.7, 674.2),
            SstvModeId.WraseMp73 => new(570.0, 140.0, 0.0, 10.0, 10.5, 140.0, 280.0, 280.0, 420.0),
            SstvModeId.WraseMp115 => new(902.0, 223.0, 0.0, 10.0, 10.5, 223.0, 446.0, 446.0, 669.0),
            SstvModeId.WraseMp140 => new(1090.0, 270.0, 0.0, 10.0, 10.5, 270.0, 540.0, 540.0, 810.0),
            SstvModeId.WraseMp175 => new(1370.0, 340.0, 0.0, 10.0, 10.5, 340.0, 680.0, 680.0, 1020.0),
            SstvModeId.WraseMl180 => new(363.3, 176.5, 88.25, 10.0, 10.6, 176.6, 264.85, 264.95, 353.2),
            SstvModeId.WraseMl240 => new(483.3, 236.5, 118.25, 10.0, 10.6, 236.6, 354.85, 354.95, 473.2),
            SstvModeId.WraseMl280 => new(565.3, 277.5, 138.75, 10.0, 10.6, 277.6, 416.35, 416.45, 555.2),
            SstvModeId.WraseMl320 => new(645.3, 317.5, 158.75, 10.0, 10.6, 317.6, 476.35, 476.45, 635.2),
            SstvModeId.Robot24 => new(200.0, 92.0, 46.0, 8.0, 8.1, 96.0, 142.0, 146.0, 192.0),
            SstvModeId.Bw8 => new(66.89709, 58.89709, 0.0, 8.0, 8.2, 58.89709, 117.79418, 117.79418, 176.69127),
            SstvModeId.Bw12 => new(100.0, 92.0, 0.0, 8.0, 8.0, 92.0, 184.0, 184.0, 276.0),
            SstvModeId.WraseMn73 => new(570.0, 140.0, 0.0, 10.0, 10.5, 140.0, 280.0, 280.0, 420.0),
            SstvModeId.WraseMn110 => new(858.0, 212.0, 0.0, 10.0, 10.5, 212.0, 424.0, 424.0, 636.0),
            SstvModeId.WraseMn140 => new(1090.0, 270.0, 0.0, 10.0, 10.5, 270.0, 540.0, 540.0, 810.0),
            SstvModeId.WraseMc110 => new(428.5, 140.0, 0.0, 8.0, 8.95, 140.0, 280.0, 280.0, 420.0),
            SstvModeId.WraseMc140 => new(548.5, 180.0, 0.0, 8.0, 8.75, 180.0, 360.0, 360.0, 540.0),
            SstvModeId.WraseMc180 => new(704.5, 232.0, 0.0, 8.0, 8.75, 232.0, 464.0, 464.0, 696.0),
            _ => new(428.22, 138.24, 0.0, 10.5, 10.7, 139.74, 277.98, 279.48, 417.72),
        };

    private static int MapSyncSamples(SstvModeProfile profile, MmsstvSourceGeometry source, int sampleRate)
        => profile.Family switch
        {
            "martin" => Samples(source.OffsetMs, sampleRate),
            "pd" => Samples(20.0, sampleRate),
            "scottie" => Samples(9.0, sampleRate),
            "robot36" => Samples(9.0, sampleRate),
            "robot" => Samples(profile.SyncMs, sampleRate),
            _ => Samples(Math.Max(0.0, profile.SyncMs), sampleRate)
        };

    private static int MapGreenStartSamples(SstvModeProfile profile, MmsstvSourceGeometry source, int sampleRate)
        => profile.Family == "scottie"
            ? 0
            : Samples(source.OffsetMs, sampleRate);

    private static int MapBlueStartSamples(SstvModeProfile profile, MmsstvSourceGeometry source, int sampleRate)
        => profile.Family switch
        {
            "scottie" => Samples(source.SyncGapMs, sampleRate),
            "robot36" => Samples(source.ChromaRedStartMs, sampleRate),
            "robot" => Samples(source.ChromaRedStartMs, sampleRate),
            _ => Samples(source.OffsetMs + source.SyncGapMs, sampleRate)
        };

    private static int MapRedStartSamples(SstvModeProfile profile, MmsstvSourceGeometry source, int sampleRate)
        => profile.Family == "scottie"
            ? Samples(source.ChromaRedStartMs + 9.0 + Math.Max(0.0, profile.GapMs), sampleRate)
            : Samples(source.OffsetMs + source.ChromaRedStartMs, sampleRate);

    private static int MapGapSamples(SstvModeProfile profile, MmsstvSourceGeometry source, int sampleRate)
        => profile.Family is "robot36" or "robot"
            ? Samples(source.ChromaGreenEndMs, sampleRate)
            : Samples(Math.Max(0.0, profile.GapMs), sampleRate);

    private static int MapSyncStartSamples(SstvModeProfile profile, MmsstvSourceGeometry source, int sampleRate)
        => profile.Family == "scottie"
            ? Samples(source.ChromaRedStartMs, sampleRate)
            : 0;

    private static int Samples(double milliseconds, int sampleRate)
        => Math.Max(0, (int)Math.Round(milliseconds * sampleRate / 1000.0));

    private static int TruncatedSamples(double milliseconds, int sampleRate)
        => Math.Max(0, (int)(milliseconds * sampleRate / 1000.0));

    private static double SourceSamples(double milliseconds, int sampleRate)
        => Math.Max(0.0, milliseconds * sampleRate / 1000.0);

    private static int PositiveSamples(double milliseconds, int sampleRate)
        => Math.Max(1, Samples(milliseconds, sampleRate));

    private sealed record MmsstvSourceGeometry(
        double TimingMs,
        double ScanMs,
        double Scan2Ms,
        double OffsetMs,
        double OffsetPreviewMs,
        double SyncGapMs,
        double ChromaGreenEndMs,
        double ChromaRedStartMs,
        double ChromaBlueStartMs)
    {
        public double ScanAdjustedMs { get; init; }
        public double Scan2AdjustedMs { get; init; }
        public double BlackAdjustMs { get; init; }
    }
}
