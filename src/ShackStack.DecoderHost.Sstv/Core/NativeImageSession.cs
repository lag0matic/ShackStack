namespace ShackStack.DecoderHost.Sstv.Core;

internal sealed class NativeImageSession
{
    private const int WorkingSampleRate = SstvWorkingConfig.WorkingSampleRate;
    private const double MinLineActivity = 0.0035;
    private readonly MmsstvDemodState _demodState;
    private readonly byte[] _rgb;
    private readonly byte[][] _rawRows;
    private readonly short[][] _rawAuxRows;
    private readonly byte[][] _stagedRgbRows;
    private readonly int[] _stagedLineNumbers;
    private readonly bool[] _stagedRowOccupied;
    private readonly List<short[]> _syncRowHistory;
    private readonly MmsstvDemodCalibration _calibration;
    private readonly MmsstvPictureGeometry _geometry;
    private readonly MmsstvAfcParameters _afcParameters;
    private readonly int[] _robotRy;
    private readonly int[] _robotBy;
    private string? _firstLineDebug;
    private string? _firstChannelDebug;
    private bool _syncBaseApplied;

    public NativeImageSession(SstvModeProfile profile, int startSample, MmsstvDemodState demodState)
    {
        Profile = profile;
        StartSample = startSample;
        _demodState = demodState;
        _calibration = MmsstvDemodCalibration.Default;
        _geometry = MmsstvPictureGeometry.Create(profile, WorkingSampleRate);
        _afcParameters = MmsstvAfcParameters.Create(profile, WorkingSampleRate);
        _rgb = new byte[profile.Width * profile.Height * 3];
        _rawRows = new byte[profile.Height][];
        _rawAuxRows = new short[profile.Height][];
        _robotRy = new int[profile.Width];
        _robotBy = new int[profile.Width];
        _stagedRgbRows = new byte[MmsstvDemodState.DemodBufferMax][];
        _stagedLineNumbers = new int[MmsstvDemodState.DemodBufferMax];
        _stagedRowOccupied = new bool[MmsstvDemodState.DemodBufferMax];
        _syncRowHistory = [];
        LastSavedLine = -1;
        NextLineStart = startSample;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        ImagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "ShackStack",
            "sstv",
            $"sstv_{timestamp}_{profile.Name.ToLowerInvariant().Replace(' ', '_')}.bmp");
    }

    public SstvModeProfile Profile { get; }
    public int StartSample { get; }
    public string ImagePath { get; }
    public int LineIndex => _demodState.WriteLine;
    public int LastSavedLine { get; private set; }
    public int NextLineStart { get; private set; }
    public bool Completed { get; private set; }
    public int ManualSlant { get; private set; }
    public int ManualOffset { get; private set; }
    public string? FirstLineDebug => _firstLineDebug;
    public string? FirstChannelDebug => _firstChannelDebug;

    public void SetManualAlignment(int manualSlant, int manualOffset)
    {
        ManualSlant = Math.Clamp(manualSlant, -200, 200);
        ManualOffset = Math.Clamp(manualOffset, -400, 400);
        RebuildImage();
    }

    public void PersistSnapshot() => SaveImage();

    public (int UpdatedLines, string? Status) DecodeAvailableLines(float[] samples)
    {
        return Profile.Family switch
        {
            "robot36" => DecodeRobot36(samples),
            "pd" => DecodePd(samples),
            "avt" => DecodeAvt(samples),
            _ => DecodeRgb(samples),
        };
    }

    public int RefineLineStart(float[] samples, int expectedStart)
    {
        if (Profile.Family == "avt")
        {
            return Math.Max(0, expectedStart);
        }

        var syncSamples = Math.Max(8, (int)Math.Round(Profile.SyncMs * WorkingSampleRate / 1000.0));
        var syncAnchorOffset = SyncAnchorOffset(syncSamples);
        var searchRange = Math.Max(24, WorkingSampleRate / 250);
        var searchStep = Math.Max(2, WorkingSampleRate / 2000);
        var bestOffset = 0;
        var bestScore = -1.0;

        for (var offset = -searchRange; offset <= searchRange; offset += searchStep)
        {
            var candidate = expectedStart + offset;
            var syncStart = candidate + syncAnchorOffset;
            if (syncStart < 0 || syncStart + syncSamples > samples.Length)
            {
                continue;
            }

            var block = samples.AsSpan(syncStart, syncSamples);
            var syncPower = SstvAudioMath.TonePower(block, WorkingSampleRate, 1200.0);
            var comparePower =
                SstvAudioMath.TonePower(block, WorkingSampleRate, 1100.0) +
                SstvAudioMath.TonePower(block, WorkingSampleRate, 1300.0) +
                SstvAudioMath.TonePower(block, WorkingSampleRate, 1500.0) +
                SstvAudioMath.TonePower(block, WorkingSampleRate, 1900.0);
            var prominence = syncPower / Math.Max(1e-9, comparePower / 4.0);
            var distancePenalty = 1.0 - (Math.Abs(offset) / (double)(searchRange * 5));
            var score = prominence * Math.Max(0.70, distancePenalty);

            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = offset;
            }
        }

        return Math.Max(0, expectedStart + bestOffset);
    }

    private (int UpdatedLines, string? Status) DecodeRgb(float[] samples)
    {
        var updated = 0;
        string? status = null;
        var lineSamples = MmsstvTimingEngine.CalculateLineSamples(Profile, WorkingSampleRate);
        var syncSamples = Math.Max(8, (int)Math.Round(Profile.SyncMs * WorkingSampleRate / 1000.0));
        var gapSamples = Math.Max(1, (int)Math.Round(Profile.GapMs * WorkingSampleRate / 1000.0));
        var scanSamples = (int)Math.Round(Profile.ScanMs * WorkingSampleRate / 1000.0);

        while (_demodState.WriteLine < Profile.Height)
        {
            var expectedStart = NextLineStart;
            if (expectedStart + lineSamples > samples.Length)
            {
                break;
            }

            var lineStart = RefineLineStart(samples, expectedStart);
            var targetLine = _demodState.WriteLine;
            if (_firstLineDebug is null && targetLine == 0)
            {
                _firstLineDebug = $"line0 start expected {expectedStart} actual {lineStart}";
            }
            if (lineStart + lineSamples > samples.Length)
            {
                break;
            }

            if (!HasLineActivity(samples.AsSpan(lineStart, lineSamples)))
            {
                break;
            }

            var lineError = lineStart - expectedStart;
            if (Profile.Family != "avt")
            {
                StageSyncRow(samples.AsSpan(lineStart, lineSamples));
            }
            var syncBiasHz = EstimateSyncBias(samples.AsSpan(lineStart, syncSamples), 1200.0);
            var channels = new Dictionary<string, byte[]>(3);
            foreach (var (color, startPos) in ChannelLayout(lineStart, syncSamples, gapSamples, scanSamples))
            {
                if (startPos < 0 || startPos + scanSamples > samples.Length)
                {
                    return (updated, status);
                }

                channels[color] = DecodeChannel(samples.AsSpan(startPos, scanSamples), syncBiasHz, color);
            }

            StageLine(channels, targetLine);
            _demodState.CommitDecodedRow(Profile.Width);
            updated += FlushStagedRows();
            NextLineStart = lineStart + lineSamples + DriftCorrection(lineError);
            status = UpdateSaveStatus();
            if (Completed)
            {
                break;
            }
        }

        return (updated, status);
    }

    private (int UpdatedLines, string? Status) DecodeRobot36(float[] samples)
    {
        var updated = 0;
        string? status = null;
        var lineSamples = MmsstvTimingEngine.CalculateLineSamples(Profile, WorkingSampleRate);
        var syncSamples = Math.Max(8, (int)Math.Round(Profile.SyncMs * WorkingSampleRate / 1000.0));
        var syncPorchSamples = Math.Max(1, (int)Math.Round(Profile.SyncPorchMs * WorkingSampleRate / 1000.0));
        var yScanSamples = (int)Math.Round(Profile.ScanMs * WorkingSampleRate / 1000.0);
        var selectorSamples = Math.Max(1, (int)Math.Round(Profile.GapMs * WorkingSampleRate / 1000.0));
        var chromaPorchSamples = Math.Max(1, (int)Math.Round(Profile.PorchMs * WorkingSampleRate / 1000.0));
        var chromaScanSamples = (int)Math.Round(Profile.AuxScanMs * WorkingSampleRate / 1000.0);

        while (_demodState.WriteLine < Profile.Height)
        {
            var expectedStart = NextLineStart;
            if (expectedStart + lineSamples > samples.Length)
            {
                break;
            }

            var lineStart = RefineLineStart(samples, expectedStart);
            var targetLine = _demodState.WriteLine;
            if (_firstLineDebug is null && targetLine == 0)
            {
                _firstLineDebug = $"line0 start expected {expectedStart} actual {lineStart}";
            }
            if (lineStart + lineSamples > samples.Length)
            {
                break;
            }

            if (!HasLineActivity(samples.AsSpan(lineStart, lineSamples)))
            {
                break;
            }

            var lineError = lineStart - expectedStart;
            var yStart = lineStart + syncSamples + syncPorchSamples;
            var selectorStart = yStart + yScanSamples;
            var cStart = selectorStart + selectorSamples + chromaPorchSamples;
            if (yStart < 0
                || selectorStart < 0
                || cStart < 0
                || yStart + yScanSamples > samples.Length
                || selectorStart + selectorSamples > samples.Length
                || cStart + chromaScanSamples > samples.Length)
            {
                return (updated, status);
            }

            StageSyncRow(samples.AsSpan(lineStart, lineSamples));
            var yPixels = DecodePictureChannel(samples.AsSpan(yStart, yScanSamples), Profile.Width, useExactSpan: true);
            var selector = ResolveRobotSelector(samples.AsSpan(selectorStart, selectorSamples), (targetLine & 1) == 0 ? 0 : 1);
            var diffPixels = DecodeSignedChannel(samples.AsSpan(cStart, chromaScanSamples), Profile.Width, useExactSpan: true);
            if (selector == 0)
            {
                Array.Copy(diffPixels, _robotRy, Profile.Width);
            }
            else
            {
                Array.Copy(diffPixels, _robotBy, Profile.Width);
            }

            StageRgbRow(targetLine, YcToRgbRow(yPixels, _robotRy, _robotBy));
            _demodState.CommitDecodedRow(Profile.Width);
            updated += FlushStagedRows();
            NextLineStart = lineStart + lineSamples + DriftCorrection(lineError);
            status = UpdateSaveStatus();
            if (Completed)
            {
                break;
            }
        }

        return (updated, status);
    }

    private (int UpdatedLines, string? Status) DecodePd(float[] samples)
    {
        var updated = 0;
        string? status = null;
        var pairSamples = _geometry.LineSamples;
        var syncSamples = _geometry.SyncSamples;
        var porchSamples = Math.Max(1, _geometry.OffsetSamples - _geometry.SyncSamples);
        var segmentSamples = _geometry.ScanSamples;

        while (_demodState.WriteLine < Profile.Height - 1)
        {
            var expectedStart = NextLineStart;
            if (expectedStart + pairSamples > samples.Length)
            {
                break;
            }

            var lineStart = RefineLineStart(samples, expectedStart);
            var targetLine = _demodState.WriteLine;
            if (_firstLineDebug is null && targetLine == 0)
            {
                _firstLineDebug = $"line0 start expected {expectedStart} actual {lineStart}";
            }
            if (lineStart + pairSamples > samples.Length)
            {
                break;
            }

            if (!HasLineActivity(samples.AsSpan(lineStart, pairSamples)))
            {
                break;
            }

            var lineError = lineStart - expectedStart;
            var y0Start = lineStart + syncSamples + porchSamples;
            var crStart = y0Start + segmentSamples;
            var cbStart = crStart + segmentSamples;
            var y1Start = cbStart + segmentSamples;
            if (y0Start < 0 || crStart < 0 || cbStart < 0 || y1Start < 0 ||
                y0Start + segmentSamples > samples.Length ||
                crStart + segmentSamples > samples.Length ||
                cbStart + segmentSamples > samples.Length ||
                y1Start + segmentSamples > samples.Length)
            {
                return (updated, status);
            }

            StageSyncRow(samples.AsSpan(lineStart, pairSamples));
            var y0 = DecodeRasterPictureChannel(samples.AsSpan(y0Start, segmentSamples), Profile.Width, _geometry.ScanSamplesAdjusted, targetLine == 0 ? "pd-y0" : null);
            var ry = DecodeRasterSignedChannel(samples.AsSpan(crStart, segmentSamples), Profile.Width, _geometry.ScanSamplesAdjusted);
            var by = DecodeRasterSignedChannel(samples.AsSpan(cbStart, segmentSamples), Profile.Width, _geometry.ScanSamplesAdjusted);
            var y1 = DecodeRasterPictureChannel(samples.AsSpan(y1Start, segmentSamples), Profile.Width, _geometry.ScanSamplesAdjusted, targetLine == 0 ? "pd-y1" : null);
            StageRgbRow(targetLine, YcToRgbRow(y0, ry, by));
            StageRgbRow(targetLine + 1, YcToRgbRow(y1, ry, by));
            _demodState.CommitDecodedRows(Profile.Width, 2);
            updated += FlushStagedRows();
            NextLineStart = lineStart + pairSamples + DriftCorrection(lineError);
            status = UpdateSaveStatus();
            if (Completed)
            {
                break;
            }
        }

        return (updated, status);
    }

    private (int UpdatedLines, string? Status) DecodeAvt(float[] samples)
    {
        var updated = 0;
        string? status = null;
        var lineSamples = MmsstvTimingEngine.CalculateLineSamples(Profile, WorkingSampleRate);
        var segmentSamples = Math.Max(1, (int)Math.Round(Profile.ScanMs * WorkingSampleRate / 1000.0));

        while (_demodState.WriteLine < Profile.Height)
        {
            var lineStart = NextLineStart;
            if (lineStart + lineSamples > samples.Length)
            {
                break;
            }

            var targetLine = _demodState.WriteLine;
            if (_firstLineDebug is null && targetLine == 0)
            {
                _firstLineDebug = $"line0 start expected {lineStart} actual {lineStart}";
            }

            if (!HasLineActivity(samples.AsSpan(lineStart, lineSamples)))
            {
                break;
            }

            var rStart = lineStart;
            var gStart = rStart + segmentSamples;
            var bStart = gStart + segmentSamples;
            if (bStart + segmentSamples > samples.Length)
            {
                break;
            }

            var auxRow = new short[lineSamples];
            PopulateAvtAuxSegment(auxRow, 0, samples.AsSpan(rStart, segmentSamples));
            PopulateAvtAuxSegment(auxRow, segmentSamples, samples.AsSpan(gStart, segmentSamples));
            PopulateAvtAuxSegment(auxRow, segmentSamples * 2, samples.AsSpan(bStart, segmentSamples));

            var channels = new Dictionary<string, byte[]>(3)
            {
                ["r"] = DecodeRasterPictureChannel(samples.AsSpan(rStart, segmentSamples), Profile.Width, _geometry.ScanSamplesAdjusted, targetLine == 0 ? "avt-r" : null),
                ["g"] = DecodeRasterPictureChannel(samples.AsSpan(gStart, segmentSamples), Profile.Width, _geometry.ScanSamplesAdjusted, targetLine == 0 ? "avt-g" : null),
                ["b"] = DecodeRasterPictureChannel(samples.AsSpan(bStart, segmentSamples), Profile.Width, _geometry.ScanSamplesAdjusted, targetLine == 0 ? "avt-b" : null),
            };

            StageAuxRow(targetLine, auxRow);
            StageLine(channels, targetLine);
            _demodState.CommitDecodedRow(Profile.Width);
            updated += FlushStagedRows();
            NextLineStart = lineStart + lineSamples;
            status = UpdateSaveStatus();
            if (Completed)
            {
                break;
            }
        }

        return (updated, status);
    }

    private int SyncAnchorOffset(int syncSamples)
    {
        if (Profile.Family != "scottie")
        {
            return 0;
        }

        var gapSamples = Math.Max(1, (int)Math.Round(Profile.GapMs * WorkingSampleRate / 1000.0));
        var scanSamples = (int)Math.Round(Profile.ScanMs * WorkingSampleRate / 1000.0);
        return (scanSamples * 2) + (gapSamples * 2);
    }

    private IEnumerable<(string Color, int StartPos)> ChannelLayout(int lineStart, int syncSamples, int gapSamples, int scanSamples)
    {
        if (Profile.Family == "scottie")
        {
            var gStart = lineStart;
            var bStart = gStart + scanSamples + gapSamples;
            var rStart = bStart + scanSamples + gapSamples + syncSamples + gapSamples;
            yield return ("g", gStart);
            yield return ("b", bStart);
            yield return ("r", rStart);
            yield break;
        }

        var firstStart = lineStart + syncSamples + gapSamples;
        var secondStart = firstStart + scanSamples + gapSamples;
        var thirdStart = secondStart + scanSamples + gapSamples;
        yield return ("g", firstStart);
        yield return ("b", secondStart);
        yield return ("r", thirdStart);
    }

    private byte[] DecodeChannel(ReadOnlySpan<float> segment, double syncBiasHz, string? debugColor = null)
    {
        var pixels = DecodeChannelPixels(segment, Profile.Width, useExactSpan: false, usePictureLevel: true, debugColor);
        if (_firstChannelDebug is null && _demodState.WriteLine == 0 && !string.IsNullOrWhiteSpace(debugColor))
        {
            _firstChannelDebug = $"{debugColor}: bias {syncBiasHz:0.0} | pixels {pixels.Min()}-{pixels.Max()}";
        }

        return DestripePixels(MedianFilter3(SmoothPixels(pixels)));
    }

    private byte[] DecodePictureChannel(ReadOnlySpan<float> segment, int width, bool useExactSpan = false, string? debugColor = null)
    {
        var pixels = DecodeChannelPixels(segment, width, useExactSpan, usePictureLevel: true, debugColor);
        return DestripePixels(MedianFilter3(SmoothPixels(pixels)));
    }

    private byte[] DecodeRasterPictureChannel(ReadOnlySpan<float> segment, int width, int effectiveScanSamples, string? debugColor = null)
    {
        var pixels = DecodeRasterChannelPixels(segment, width, effectiveScanSamples, usePictureLevel: true, debugColor);
        return DestripePixels(MedianFilter3(SmoothPixels(pixels)));
    }

    private int[] DecodeSignedChannel(ReadOnlySpan<float> segment, int width, bool useExactSpan = false)
    {
        if (segment.Length < 8)
        {
            return new int[width];
        }

        var demod = DemodulateChannel(segment);
        if (demod.Length == 0)
        {
            return new int[width];
        }

        var pixels = new int[width];
        var effectiveScanSamples = Math.Clamp(useExactSpan ? demod.Length : _geometry.ScanSamplesAdjusted, 1, demod.Length);
        for (var idx = 0; idx < width; idx++)
        {
            var position = ((idx + 0.5) * effectiveScanSamples / width) - 0.5;
            pixels[idx] = GetSignedPixelLevel(SampleInterpolated(demod, position), _calibration);
        }

        return pixels;
    }

    private int[] DecodeRasterSignedChannel(ReadOnlySpan<float> segment, int width, int effectiveScanSamples)
    {
        if (segment.Length < 8)
        {
            return new int[width];
        }

        var demod = DemodulateChannel(segment);
        if (demod.Length == 0)
        {
            return new int[width];
        }

        var pixels = new int[width];
        var written = new bool[width];
        var usableSamples = Math.Clamp(effectiveScanSamples, 1, demod.Length);
        var lastX = -1;
        for (var ps = 0; ps < usableSamples; ps++)
        {
            var x = (ps * width) / usableSamples;
            if (x == lastX || x < 0 || x >= width)
            {
                continue;
            }

            lastX = x;
            pixels[x] = GetSignedPixelLevel(demod[ps], _calibration);
            written[x] = true;
        }

        FillRasterGaps(pixels, written);
        return pixels;
    }

    private int ResolveRobotSelector(ReadOnlySpan<float> segment, int fallbackSelector)
    {
        if (segment.Length < 8)
        {
            return fallbackSelector;
        }

        var demod = DemodulateChannel(segment);
        if (demod.Length == 0)
        {
            return fallbackSelector;
        }

        var selectorLevel = GetSignedPixelLevel(demod.Average(), _calibration);
        if (selectorLevel >= 64)
        {
            return 1;
        }

        if (selectorLevel < -64)
        {
            return 0;
        }

        return fallbackSelector;
    }

    private byte[] DecodeChannelPixels(
        ReadOnlySpan<float> segment,
        int width,
        bool useExactSpan,
        bool usePictureLevel,
        string? debugColor = null)
    {
        if (segment.Length < 8)
        {
            return new byte[width];
        }

        var demod = DemodulateChannel(segment);
        if (demod.Length == 0)
        {
            return new byte[width];
        }

        var pixels = new byte[width];
        var effectiveScanSamples = Math.Clamp(useExactSpan ? demod.Length : _geometry.ScanSamplesAdjusted, 1, demod.Length);
        var blackAdjustSamples = _geometry.BlackAdjustSamples;
        for (var idx = 0; idx < width; idx++)
        {
            var position = ((idx + 0.5) * effectiveScanSamples / width) - 0.5;
            var sample = usePictureLevel
                ? GetPictureLevel(demod, position, blackAdjustSamples)
                : SampleInterpolated(demod, position);
            pixels[idx] = GetPicturePixelLevel(sample, _calibration);
        }

        if (_firstChannelDebug is null && _demodState.WriteLine == 0 && !string.IsNullOrWhiteSpace(debugColor))
        {
            _firstChannelDebug =
                $"{debugColor}: demod {demod.Min():0.000}-{demod.Max():0.000} | pixels {pixels.Min()}-{pixels.Max()} | span {effectiveScanSamples}";
        }

        return pixels;
    }

    private byte[] DecodeRasterChannelPixels(
        ReadOnlySpan<float> segment,
        int width,
        int effectiveScanSamples,
        bool usePictureLevel,
        string? debugColor = null)
    {
        if (segment.Length < 8)
        {
            return new byte[width];
        }

        var demod = DemodulateChannel(segment);
        if (demod.Length == 0)
        {
            return new byte[width];
        }

        var pixels = new byte[width];
        var written = new bool[width];
        var usableSamples = Math.Clamp(effectiveScanSamples, 1, demod.Length);
        var blackAdjustSamples = _geometry.BlackAdjustSamples;
        var lastX = -1;
        for (var ps = 0; ps < usableSamples; ps++)
        {
            var x = (ps * width) / usableSamples;
            if (x == lastX || x < 0 || x >= width)
            {
                continue;
            }

            lastX = x;
            var sample = usePictureLevel
                ? GetPictureLevel(demod, ps, blackAdjustSamples)
                : demod[ps];
            pixels[x] = GetPicturePixelLevel(sample, _calibration);
            written[x] = true;
        }

        FillRasterGaps(pixels, written);

        if (_firstChannelDebug is null && _demodState.WriteLine == 0 && !string.IsNullOrWhiteSpace(debugColor))
        {
            _firstChannelDebug =
                $"{debugColor}: demod {demod.Min():0.000}-{demod.Max():0.000} | pixels {pixels.Min()}-{pixels.Max()} | raster {usableSamples}";
        }

        return pixels;
    }

    private double[] DemodulateChannel(ReadOnlySpan<float> segment)
        => DemodulateChannel(segment, out _);

    private double[] DemodulateChannel(ReadOnlySpan<float> segment, out double[] rawDemod)
    {
        var demodulators = new MmsstvDemodulatorBank(WorkingSampleRate, Profile.Narrow);
        var afc = new MmsstvAfcTracker();
        var level = new MmsstvLevelTracker(WorkingSampleRate) { FastAgc = true };
        afc.Configure(_afcParameters);
        var demodType = MmsstvDemodulatorType.ZeroCrossing;
        var allowAfcTracking = !string.Equals(Profile.Family, "avt", StringComparison.OrdinalIgnoreCase);

        rawDemod = new double[segment.Length];
        var demod = new double[segment.Length];
        for (var i = 0; i < segment.Length; i++)
        {
            level.Process(segment[i] * 16384.0);
            level.Fix();

            var raw = demodulators.ProcessRaw(segment[i], demodType);
            if (allowAfcTracking && level.CurrentMax > 16.0)
            {
                afc.Update(raw, _afcParameters.AfcBeginSamples, _afcParameters.AfcEndSamples);
                raw += afc.AfcDiff;
            }

            rawDemod[i] = raw;
            demod[i] = demodulators.NormalizeRaw(raw, demodType);
        }

        SmoothFrequency(demod);
        return demod;
    }

    private void PopulateAvtAuxSegment(short[] auxRow, int offset, ReadOnlySpan<float> segment)
    {
        if (!string.Equals(Profile.Family, "avt", StringComparison.OrdinalIgnoreCase) ||
            auxRow.Length == 0 ||
            segment.Length < 8 ||
            offset >= auxRow.Length)
        {
            return;
        }

        DemodulateChannel(segment, out var rawDemod);
        var usable = Math.Min(rawDemod.Length, auxRow.Length - offset);
        for (var i = 0; i < usable; i++)
        {
            var value = (rawDemod[i] + 16384.0) * 0.25;
            auxRow[offset + i] = (short)Math.Clamp((int)Math.Round(value), 0, short.MaxValue);
        }
    }

    private static double EstimateSyncBias(ReadOnlySpan<float> syncSegment, double expectedHz)
    {
        if (syncSegment.Length < 8)
        {
            return 0.0;
        }

        var instFreq = SstvAudioMath.InstantaneousFrequency(syncSegment, WorkingSampleRate);
        if (instFreq.Length == 0)
        {
            return 0.0;
        }

        var average = instFreq.Average();
        return average - expectedHz;
    }

    private static void SmoothFrequency(double[] values)
    {
        if (values.Length < 7)
        {
            return;
        }

        var source = (double[])values.Clone();
        var kernel = new[] { 0.06, 0.10, 0.16, 0.36, 0.16, 0.10, 0.06 };
        for (var i = 0; i < values.Length; i++)
        {
            var sum = 0.0;
            for (var k = -3; k <= 3; k++)
            {
                var index = Math.Clamp(i + k, 0, source.Length - 1);
                sum += source[index] * kernel[k + 3];
            }

            values[i] = sum;
        }
    }

    private static byte[] SmoothPixels(byte[] pixels)
    {
        if (pixels.Length < 5)
        {
            return pixels;
        }

        var result = new byte[pixels.Length];
        var kernel = new[] { 0.1, 0.2, 0.4, 0.2, 0.1 };
        for (var i = 0; i < pixels.Length; i++)
        {
            var sum = 0.0;
            for (var k = -2; k <= 2; k++)
            {
                var index = Math.Clamp(i + k, 0, pixels.Length - 1);
                sum += pixels[index] * kernel[k + 2];
            }

            result[i] = (byte)Math.Clamp((int)Math.Round(sum), 0, 255);
        }

        return result;
    }

    private static byte[] DestripePixels(byte[] pixels)
    {
        if (pixels.Length < 3)
        {
            return pixels;
        }

        var corrected = (byte[])pixels.Clone();
        for (var idx = 1; idx < pixels.Length - 1; idx++)
        {
            var left = corrected[idx - 1];
            var center = corrected[idx];
            var right = corrected[idx + 1];
            if (Math.Abs(center - left) > 90 && Math.Abs(center - right) > 90 && Math.Abs(left - right) < 45)
            {
                corrected[idx] = (byte)((left + right) / 2);
            }
        }

        return corrected;
    }


    private static byte[] MedianFilter3(byte[] pixels)
    {
        if (pixels.Length < 3)
        {
            return pixels;
        }

        var filtered = (byte[])pixels.Clone();
        for (var idx = 1; idx < pixels.Length - 1; idx++)
        {
            var a = pixels[idx - 1];
            var b = pixels[idx];
            var c = pixels[idx + 1];
            if (a > b)
            {
                (a, b) = (b, a);
            }

            if (b > c)
            {
                (b, c) = (c, b);
            }

            if (a > b)
            {
                (a, b) = (b, a);
            }

            filtered[idx] = b;
        }

        return filtered;
    }

    private static double SampleInterpolated(double[] values, double position)
    {
        if (values.Length == 0)
        {
            return 0.0;
        }

        if (position <= 0)
        {
            return values[0];
        }

        if (position >= values.Length - 1)
        {
            return values[^1];
        }

        var left = (int)Math.Floor(position);
        var right = Math.Min(values.Length - 1, left + 1);
        var fraction = position - left;
        return values[left] + ((values[right] - values[left]) * fraction);
    }

    private static double GetPictureLevel(double[] values, double position, int blackAdjustSamples)
    {
        var current = SampleInterpolated(values, position);
        if (blackAdjustSamples <= 0)
        {
            return current;
        }

        var adjusted = SampleInterpolated(values, position + blackAdjustSamples);
        return current < adjusted ? adjusted : current;
    }

    private static double GetPictureLevelDiff(
        double[] values,
        double position,
        int blackAdjustSamples,
        ref double z0,
        ref double z1,
        ref double z2,
        double diffLevelPositive = 1.0,
        double diffLevelNegative = 1.0 / 3.0)
    {
        var sample = GetPictureLevel(values, position, blackAdjustSamples);
        var diff = (sample * -0.5) + z0 + (z1 * -0.5);
        diff *= diff > 0.0 ? diffLevelPositive : diffLevelNegative;
        diff += z2;
        z1 = z0;
        z2 = z0;
        z0 = sample;
        return diff;
    }

    private static void FillRasterGaps(byte[] pixels, bool[] written)
    {
        var last = -1;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (!written[i])
            {
                continue;
            }

            if (last < 0)
            {
                for (var j = 0; j < i; j++)
                {
                    pixels[j] = pixels[i];
                }
            }
            else if (i - last > 1)
            {
                for (var j = last + 1; j < i; j++)
                {
                    pixels[j] = pixels[last];
                }
            }

            last = i;
        }

        if (last >= 0)
        {
            for (var i = last + 1; i < pixels.Length; i++)
            {
                pixels[i] = pixels[last];
            }
        }
    }

    private static void FillRasterGaps(int[] pixels, bool[] written)
    {
        var last = -1;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (!written[i])
            {
                continue;
            }

            if (last < 0)
            {
                for (var j = 0; j < i; j++)
                {
                    pixels[j] = pixels[i];
                }
            }
            else if (i - last > 1)
            {
                for (var j = last + 1; j < i; j++)
                {
                    pixels[j] = pixels[last];
                }
            }

            last = i;
        }

        if (last >= 0)
        {
            for (var i = last + 1; i < pixels.Length; i++)
            {
                pixels[i] = pixels[last];
            }
        }
    }

    private void StageLine(Dictionary<string, byte[]> channels, int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= Profile.Height)
        {
            return;
        }

        var row = new byte[Profile.Width * 3];
        var red = channels.TryGetValue("r", out var r) ? r : new byte[Profile.Width];
        var green = channels.TryGetValue("g", out var g) ? g : new byte[Profile.Width];
        var blue = channels.TryGetValue("b", out var b) ? b : new byte[Profile.Width];

        for (var x = 0; x < Profile.Width; x++)
        {
            var offset = x * 3;
            row[offset] = red[x];
            row[offset + 1] = green[x];
            row[offset + 2] = blue[x];
        }

        StageRgbRow(lineIndex, row);
    }

    private void StageRgbRow(int lineIndex, byte[] row)
    {
        if (lineIndex < 0 || lineIndex >= Profile.Height)
        {
            return;
        }

        var page = _demodState.WritePage;
        _stagedRgbRows[page] = (byte[])row.Clone();
        _stagedLineNumbers[page] = lineIndex;
        _stagedRowOccupied[page] = true;
        _demodState.MarkStagedRow();
    }

    private int FlushStagedRows()
    {
        var flushed = 0;
        while (_demodState.ReadPage != _demodState.WritePage)
        {
            var page = _demodState.ReadPage;
            if (_stagedRowOccupied[page] && _stagedRgbRows[page] is { } row)
            {
                WriteRgbRow(_stagedLineNumbers[page], row);
                _stagedRgbRows[page] = null!;
                _stagedRowOccupied[page] = false;
                flushed++;
            }

            _demodState.AdvanceReadPointer(Profile.Width);
        }

        TryApplySyncBufferAlignment();
        return flushed;
    }

    private void StageSyncRow(ReadOnlySpan<float> lineSegment)
    {
        if (lineSegment.Length <= 0)
        {
            return;
        }

        var filters = new MmsstvSyncFilterBank(WorkingSampleRate);
        filters.Retune(_demodState.SyncToneBank);
        var row = new short[lineSegment.Length];
        for (var i = 0; i < lineSegment.Length; i++)
        {
            var snapshot = filters.ProcessSample(lineSegment[i]);
            var compare = Math.Max(snapshot.Tone1080, Math.Max(snapshot.Tone1320, snapshot.Tone1900));
            var emphasis = Math.Max(0.0, snapshot.Tone1200 - compare);
            row[i] = (short)Math.Clamp((int)Math.Round(emphasis * 4.0), 0, short.MaxValue);
        }

        _syncRowHistory.Add(row);
        if (_syncRowHistory.Count > 16)
        {
            _syncRowHistory.RemoveAt(0);
        }
    }

    private void TryApplySyncBufferAlignment()
    {
        if (_syncBaseApplied || _syncRowHistory.Count < 4 || _demodState.StageWriteLine < 4)
        {
            return;
        }

        if (!MmsstvSyncBufferAligner.TryComputeBaseOffset(
                _syncRowHistory,
                Profile,
                WorkingSampleRate,
                _geometry.LineSamples,
                _geometry.OffsetPreviewSamples,
                _demodState.StageWriteLine,
                useRxBuffer: true,
                highAccuracy: true,
                out var baseOffset))
        {
            return;
        }

        var normalizedOffset = NormalizeWrappedOffset(baseOffset, _geometry.LineSamples);
        if (Math.Abs(normalizedOffset) < 2 || Math.Abs(normalizedOffset) > (_geometry.LineSamples / 4))
        {
            return;
        }

        NextLineStart += normalizedOffset;
        _syncBaseApplied = true;
    }

    private static int NormalizeWrappedOffset(int offset, int modulus)
    {
        if (modulus <= 0)
        {
            return offset;
        }

        var normalized = offset % modulus;
        if (normalized > modulus / 2)
        {
            normalized -= modulus;
        }
        else if (normalized < -(modulus / 2))
        {
            normalized += modulus;
        }

        return normalized;
    }

    private void WriteRgbRow(int lineIndex, byte[] row)
    {
        if (lineIndex >= Profile.Height)
        {
            return;
        }

        _rawRows[lineIndex] = (byte[])row.Clone();
        var adjusted = ApplyAlignment(row, lineIndex);
        Buffer.BlockCopy(adjusted, 0, _rgb, lineIndex * Profile.Width * 3, adjusted.Length);
    }

    private void StageAuxRow(int lineIndex, short[] row)
    {
        if (lineIndex < 0 || lineIndex >= Profile.Height)
        {
            return;
        }

        _rawAuxRows[lineIndex] = (short[])row.Clone();
    }

    private byte[] ApplyAlignment(byte[] row, int lineIndex)
    {
        var shift = ManualOffset;
        if (ManualSlant != 0 && lineIndex > 0)
        {
            shift += (int)Math.Round(lineIndex * (ManualSlant / 1000.0));
        }

        if (shift == 0)
        {
            return row;
        }

        var pixelShift = ((shift % Profile.Width) + Profile.Width) % Profile.Width;
        var adjusted = new byte[row.Length];
        for (var x = 0; x < Profile.Width; x++)
        {
            var srcPixel = ((x - pixelShift) + Profile.Width) % Profile.Width;
            Buffer.BlockCopy(row, srcPixel * 3, adjusted, x * 3, 3);
        }

        return adjusted;
    }

    private void RebuildImage()
    {
        Array.Clear(_rgb);
        for (var lineIndex = 0; lineIndex < _rawRows.Length; lineIndex++)
        {
            var row = _rawRows[lineIndex];
            if (row is null)
            {
                continue;
            }

            var adjusted = ApplyAlignment(row, lineIndex);
            Buffer.BlockCopy(adjusted, 0, _rgb, lineIndex * Profile.Width * 3, adjusted.Length);
        }

        SaveImage();
    }

    private string? UpdateSaveStatus()
    {
        if (_demodState.WriteLine >= Profile.Height)
        {
            Completed = true;
            SaveImage();
            return $"Image complete: {Path.GetFileName(ImagePath)}";
        }

        if ((_demodState.WriteLine - LastSavedLine) >= 8)
        {
            SaveImage();
            LastSavedLine = _demodState.WriteLine;
            return $"Decoded {_demodState.WriteLine}/{Profile.Height} lines";
        }

        return null;
    }

    private void SaveImage()
    {
        NativeBitmapWriter.SaveRgb24(ImagePath, _rgb, Profile.Width, Profile.Height);
    }

    private static byte GetPicturePixelLevel(double value, MmsstvDemodCalibration calibration)
    {
        var d = value - calibration.Offset;
        d *= d >= 0.0 ? calibration.WhiteGain : calibration.BlackGain;
        d += 128.0;
        return (byte)Math.Clamp((int)Math.Round(d), 0, 255);
    }


    private static int GetSignedPixelLevel(double value, MmsstvDemodCalibration calibration)
    {
        var d = value - calibration.Offset;
        d *= d >= 0.0 ? calibration.WhiteGain : calibration.BlackGain;
        return Math.Clamp((int)Math.Round(d), -255, 255);
    }

    private int DriftCorrection(int lineError)
    {
        return Math.Clamp((int)Math.Round(lineError * 0.35), -24, 24);
    }

    private static bool HasLineActivity(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return false;
        }

        double sum = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += Math.Abs(samples[i]);
        }

        return (sum / samples.Length) >= MinLineActivity;
    }

    private static byte[] YcToRgbRow(byte[] y, int[] ry, int[] by)
    {
        var row = new byte[y.Length * 3];
        for (var x = 0; x < y.Length; x++)
        {
            var yv = y[x] - 16.0;
            var r = Math.Clamp((int)Math.Round((1.164457 * yv) + (1.596128 * ry[x])), 0, 255);
            var g = Math.Clamp((int)Math.Round((1.164457 * yv) - (0.813022 * ry[x]) - (0.391786 * by[x])), 0, 255);
            var b = Math.Clamp((int)Math.Round((1.164457 * yv) + (2.017364 * by[x])), 0, 255);
            var offset = x * 3;
            row[offset] = (byte)r;
            row[offset + 1] = (byte)g;
            row[offset + 2] = (byte)b;
        }

        return row;
    }
}
