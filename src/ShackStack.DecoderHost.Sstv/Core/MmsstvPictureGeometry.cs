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
    int BlackAdjustSamples)
{
    public static MmsstvPictureGeometry Create(SstvModeProfile profile, int sampleRate)
    {
        var lineSamples = (int)Math.Round(profile.TimingMs * sampleRate / 1000.0);

        if (profile.Family == "martin")
        {
            var ks = (int)Math.Round(profile.ScanMs * sampleRate / 1000.0);
            var of = (int)Math.Round(profile.SyncMs * sampleRate / 1000.0);
            var ofp = (int)Math.Round((profile.SyncMs + (profile.GapMs * 0.5)) * sampleRate / 1000.0);
            var sg = (int)Math.Round((profile.ScanMs + profile.GapMs) * sampleRate / 1000.0);
            var cg = ks + sg;
            var sb = sg + sg;
            var cb = ks + sb;
            var kss = (int)Math.Round(ks - (ks / 240.0));
            var ksb = Math.Max(1, (int)Math.Round(kss / 640.0));

            return new MmsstvPictureGeometry(
                lineSamples,
                SyncSamples: of,
                OffsetSamples: of,
                OffsetPreviewSamples: ofp,
                ScanSamples: ks,
                ScanSamplesAdjusted: kss,
                Scan2Samples: 0,
                Scan2SamplesAdjusted: 0,
                GreenStartSamples: of + Math.Max(1, (int)Math.Round(profile.GapMs * sampleRate / 1000.0)),
                BlueStartSamples: of + Math.Max(1, (int)Math.Round(profile.GapMs * sampleRate / 1000.0)) + sg,
                RedStartSamples: of + Math.Max(1, (int)Math.Round(profile.GapMs * sampleRate / 1000.0)) + sb,
                BlackAdjustSamples: ksb);
        }

        if (profile.Family == "scottie")
        {
            var ks = (int)Math.Round(profile.ScanMs * sampleRate / 1000.0);
            var of = (int)Math.Round(10.5 * sampleRate / 1000.0);
            var ofp = profile.Name switch
            {
                "Scottie 1" => (int)Math.Round(10.7 * sampleRate / 1000.0),
                "Scottie 2" => (int)Math.Round(10.8 * sampleRate / 1000.0),
                "Scottie DX" => (int)Math.Round(10.2 * sampleRate / 1000.0),
                _ => (int)Math.Round((profile.SyncMs + profile.GapMs) * sampleRate / 1000.0),
            };
            var sg = (int)Math.Round((profile.ScanMs + profile.GapMs) * sampleRate / 1000.0);
            var cg = ks + sg;
            var sb = sg + sg;
            var cb = ks + sb;
            var kss = profile.Name == "Scottie DX"
                ? (int)Math.Round(ks - (ks / 1280.0))
                : (int)Math.Round(ks - (ks / 240.0));
            var ksb = Math.Max(1, (int)Math.Round(kss / (profile.Name == "Scottie DX" ? 1280.0 : 640.0)));

            return new MmsstvPictureGeometry(
                lineSamples,
                SyncSamples: (int)Math.Round(profile.SyncMs * sampleRate / 1000.0),
                OffsetSamples: of,
                OffsetPreviewSamples: ofp,
                ScanSamples: ks,
                ScanSamplesAdjusted: kss,
                Scan2Samples: 0,
                Scan2SamplesAdjusted: 0,
                GreenStartSamples: 0,
                BlueStartSamples: sg,
                RedStartSamples: cb,
                BlackAdjustSamples: ksb);
        }

        if (profile.Family == "pd")
        {
            var (ksMs, ofpMs, useWideAdjust) = profile.Name switch
            {
                "PD 50" => (91.520, 19.300, false),
                "PD 90" => (170.240, 18.900, false),
                "PD 120" => (121.600, 19.400, true),
                "PD 160" => (195.584, 18.900, true),
                "PD 180" => (183.040, 18.900, true),
                "PD 240" => (244.480, 18.900, true),
                "PD 290" => (228.800, 18.900, true),
                _ => (profile.PixelMs * profile.Width, profile.SyncMs + profile.PorchMs, profile.Width >= 512),
            };

            var ks = Math.Max(1, (int)Math.Round(ksMs * sampleRate / 1000.0));
            var of = Math.Max(1, (int)Math.Round((profile.SyncMs + profile.PorchMs) * sampleRate / 1000.0));
            var ofp = Math.Max(1, (int)Math.Round(ofpMs * sampleRate / 1000.0));
            var sg = ks;
            var cg = ks + sg;
            var sb = sg + sg;
            var cb = ks + sb;
            var kss = useWideAdjust
                ? (int)Math.Round(ks - (ks / 480.0))
                : (int)Math.Round(ks - (ks / 240.0));
            var ksb = Math.Max(1, (int)Math.Round(kss / (useWideAdjust ? 1280.0 : 640.0)));

            return new MmsstvPictureGeometry(
                LineSamples: Math.Max(1, (int)Math.Round(profile.TimingMs * sampleRate / 1000.0)),
                SyncSamples: Math.Max(1, (int)Math.Round(profile.SyncMs * sampleRate / 1000.0)),
                OffsetSamples: of,
                OffsetPreviewSamples: ofp,
                ScanSamples: ks,
                ScanSamplesAdjusted: kss,
                Scan2Samples: 0,
                Scan2SamplesAdjusted: 0,
                GreenStartSamples: of,
                BlueStartSamples: of + sg,
                RedStartSamples: of + sb,
                BlackAdjustSamples: ksb);
        }

        if (profile.Family == "avt")
        {
            var ks = Math.Max(1, (int)Math.Round(profile.ScanMs * sampleRate / 1000.0));
            var kss = Math.Max(1, (int)Math.Round(ks - (ks / 240.0)));
            var ksb = Math.Max(1, (int)Math.Round(kss / 640.0));
            return new MmsstvPictureGeometry(
                lineSamples,
                SyncSamples: 0,
                OffsetSamples: 0,
                OffsetPreviewSamples: 0,
                ScanSamples: ks,
                ScanSamplesAdjusted: kss,
                Scan2Samples: 0,
                Scan2SamplesAdjusted: 0,
                GreenStartSamples: ks,
                BlueStartSamples: ks * 2,
                RedStartSamples: 0,
                BlackAdjustSamples: ksb);
        }

        var fallbackSync = Math.Max(1, (int)Math.Round(profile.SyncMs * sampleRate / 1000.0));
        var fallbackScan = Math.Max(1, (int)Math.Round(profile.ScanMs * sampleRate / 1000.0));
        return new MmsstvPictureGeometry(
            lineSamples,
            fallbackSync,
            fallbackSync,
            fallbackSync,
            fallbackScan,
            fallbackScan,
            Math.Max(0, (int)Math.Round(profile.AuxScanMs * sampleRate / 1000.0)),
            Math.Max(0, (int)Math.Round(profile.AuxScanMs * sampleRate / 1000.0)),
            fallbackSync,
            fallbackSync + fallbackScan,
            fallbackSync + fallbackScan + fallbackScan,
            1);
    }
}
