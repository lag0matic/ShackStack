namespace ShackStack.DecoderHost.Sstv.Core;

internal sealed class NativeImageSession
{
    private const int WorkingSampleRate = SstvWorkingConfig.WorkingSampleRate;
    private const double MinLineActivity = 0.0035;
    private const int MmsstvSlantCorrectionMinRows = 16;
    private const int MmsstvSyncUnset = int.MaxValue;
    private static readonly bool UseMmsstvFullRxForRgb =
        !string.Equals(Environment.GetEnvironmentVariable("SHACKSTACK_SSTV_USE_MMSSTV_FULL_RX"), "0", StringComparison.Ordinal);
    private static readonly bool UseMmsstvDoubleDrawClock =
        !string.Equals(Environment.GetEnvironmentVariable("SHACKSTACK_SSTV_USE_MMSSTV_DOUBLE_DRAW"), "0", StringComparison.Ordinal);
    private static readonly bool UseMmsstvSlantCorrection =
        string.Equals(Environment.GetEnvironmentVariable("SHACKSTACK_SSTV_USE_MMSSTV_SLANT_CORRECTION"), "1", StringComparison.Ordinal);
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
    private readonly MmsstvReceiveBuffer _receiveBuffer;
    private readonly bool _mmsstvAutoSync;
    private readonly bool _mmsstvAutoStop;
    private readonly int[] _robotRy;
    private readonly int[] _robotBy;
    private readonly int[] _mmsstvDrawLuma;
    private readonly int[] _mmsstvDrawChannel0;
    private readonly int[] _mmsstvDrawChannel1;
    private readonly int[] _mmsstvAutoStopPositions;
    private readonly int[] _mmsstvAutoSlantPositions;
    private readonly double[] _mmsstvAutoSlantLimits;
    private readonly MmsstvMovingAverage _mmsstvAutoSlantAverage;
    private readonly List<MmsstvReceiveBuffer.ReceivePage> _pendingMmsstvPages;
    private readonly List<short[]> _mmsstvStagedPictureRows;
    private readonly List<short[]> _mmsstvStagedSyncRows;
    private string? _firstLineDebug;
    private string? _firstChannelDebug;
    private string? _mmsstvSlantDebug;
    private string? _mmsstvSyncAdjustDebug;
    private bool _syncBaseApplied;
    private bool _mmsstvDrawBaseInitialized;
    private bool _mmsstvSlantCorrectionApplied;
    private int _mmsstvDrawBase;
    private int _mmsstvDrawStartBase;
    private double _mmsstvDrawLineSamples;
    private double _mmsstvDrawScale = 1.0;
    private int _mmsstvDrawAxisX = -1;
    private int _mmsstvDrawSelector;
    private int _liveReceiveBufferAppendCursor = -1;
    private int _fullRxPageIndex;
    private int _mmsstvSyncPos = -1;
    private int _mmsstvSyncRPos = -1;
    private int _mmsstvSyncMin;
    private int _mmsstvSyncMax;
    private int _mmsstvAutoStopPos;
    private int _mmsstvAutoStopCount;
    private int _mmsstvAutoStopAverageCount;
    private int _mmsstvAutoSyncPos = MmsstvSyncUnset;
    private int _mmsstvAutoSyncDisable;
    private int _mmsstvAutoSyncCount;
    private double _mmsstvAutoSlantBeginPos = MmsstvSyncUnset;
    private int _mmsstvAutoSlantDisable;
    private int _mmsstvAutoSlantBitMask;
    private int _mmsstvAutoSlantY;

    public NativeImageSession(
        SstvModeProfile profile,
        int startSample,
        MmsstvDemodState demodState,
        bool syncRestart,
        bool autoSync,
        bool autoStop)
    {
        Profile = profile;
        StartSample = startSample;
        _demodState = demodState;
        _mmsstvAutoSync = autoSync;
        _mmsstvAutoStop = autoStop;
        _calibration = MmsstvDemodCalibration.Default;
        _geometry = MmsstvPictureGeometry.Create(profile, WorkingSampleRate);
        _afcParameters = MmsstvAfcParameters.Create(profile, WorkingSampleRate);
        _receiveBuffer = new MmsstvReceiveBuffer(profile, WorkingSampleRate, syncRestart);
        _mmsstvDrawLineSamples = _geometry.DrawLineSamples;
        _rgb = new byte[profile.Width * profile.Height * 3];
        _rawRows = new byte[profile.Height][];
        _rawAuxRows = new short[profile.Height][];
        _robotRy = new int[profile.Width];
        _robotBy = new int[profile.Width];
        _mmsstvDrawLuma = new int[profile.Width];
        _mmsstvDrawChannel0 = new int[profile.Width];
        _mmsstvDrawChannel1 = new int[profile.Width];
        _mmsstvAutoStopPositions = new int[16];
        _mmsstvAutoSlantPositions = CreateMmsstvAutoSlantPositions(profile);
        _mmsstvAutoSlantLimits = CreateMmsstvAutoSlantLimits(WorkingSampleRate);
        _mmsstvAutoSlantAverage = new MmsstvMovingAverage(16);
        _mmsstvAutoSlantDisable = 0;
        _pendingMmsstvPages = [];
        _mmsstvStagedPictureRows = [];
        _mmsstvStagedSyncRows = [];
        _stagedRgbRows = new byte[MmsstvDemodState.DemodBufferMax][];
        _stagedLineNumbers = new int[MmsstvDemodState.DemodBufferMax];
        _stagedRowOccupied = new bool[MmsstvDemodState.DemodBufferMax];
        _syncRowHistory = [];
        LastSavedLine = -1;
        NextLineStart = startSample;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var archiveRoot = Environment.GetEnvironmentVariable("SHACKSTACK_SSTV_ARCHIVE_DIR");
        if (string.IsNullOrWhiteSpace(archiveRoot))
        {
            archiveRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ShackStack",
                "sstv");
        }

        ImagePath = Path.Combine(
            archiveRoot,
            $"sstv_{timestamp}_{profile.Name.ToLowerInvariant().Replace(' ', '_')}.bmp");
    }

    public SstvModeProfile Profile { get; }
    public int StartSample { get; }
    public string ImagePath { get; }
    public int LineIndex => _demodState.WriteLine;
    public int LastSavedLine { get; private set; }
    public int NextLineStart { get; private set; }
    public bool Completed { get; private set; }
    public string? FirstLineDebug => _firstLineDebug;
    public string? FirstChannelDebug => _firstChannelDebug;
    public string? MmsstvSlantDebug => _mmsstvSlantDebug;
    public string? MmsstvSyncAdjustDebug => _mmsstvSyncAdjustDebug;

    private bool UsesMmsstvBufferedDraw
        => UseMmsstvFullRxForRgb && IsMmsstvFullRxProfile(Profile);

    public void ApplyMmsstvReceiveSkip(int sampleCount)
    {
        if (!UseMmsstvFullRxForRgb || !IsMmsstvFullRxProfile(Profile))
        {
            return;
        }

        _receiveBuffer.SetSkipSamples(sampleCount);
    }

    public bool ApplyMmsstvLiveSyncSkip(out string debug)
    {
        debug = "live-sync: skipped";
        if (!UseMmsstvFullRxForRgb || !IsMmsstvFullRxProfile(Profile) || Profile.Id == SstvModeId.Avt90)
        {
            return false;
        }

        if (_mmsstvSyncRPos < 0)
        {
            debug = $"live-sync: no reference sync pos current {_mmsstvSyncPos}";
            return false;
        }

        var offsetPreview = (int)(_geometry.DrawOffsetPreviewSamples * _mmsstvDrawScale);
        if (Math.Abs(_mmsstvSyncPos - offsetPreview) < 5)
        {
            debug = $"live-sync: within threshold pos {_mmsstvSyncPos} ofp {offsetPreview}";
            return false;
        }

        var lineWidth = Math.Max(1, (int)_mmsstvDrawLineSamples);
        var skip = _mmsstvSyncRPos - offsetPreview;
        if (skip < 0)
        {
            skip += lineWidth;
        }

        var syncPos = _mmsstvSyncPos;
        var syncReferencePos = _mmsstvSyncRPos;
        _receiveBuffer.SetSkipSamples(skip);
        _mmsstvSyncPos = -1;
        _mmsstvSyncRPos = -1;
        _mmsstvAutoSyncPos = MmsstvSyncUnset;
        _mmsstvAutoStopCount = 0;
        _mmsstvAutoStopAverageCount = 0;
        _mmsstvAutoSyncDisable = 6;
        _mmsstvAutoSyncCount++;
        debug = $"live-sync: skip {skip} pos {syncPos} rpos {syncReferencePos} ofp {offsetPreview}";
        return true;
    }

    public void PersistSnapshot() => SaveImage();

    public (int UpdatedLines, string? Status) DecodeAvailableLines(float[] samples)
    {
        if (UseMmsstvFullRxForRgb && IsMmsstvFullRxProfile(Profile))
        {
            return DrainMmsstvFullRx();
        }

        return Profile.Family switch
        {
            "robot36" => DecodeRobot36(samples),
            "pd" => DecodePd(samples),
            "avt" => DecodeAvt(samples),
            _ => DecodeRgb(samples),
        };
    }

    public void SeedMmsstvFullRx(float[] samples)
    {
        if (!UseMmsstvFullRxForRgb || !IsMmsstvFullRxProfile(Profile))
        {
            return;
        }

        AppendLiveMmsstvSamples(samples, 0);
    }

    public void AppendLiveMmsstvSamples(ReadOnlySpan<float> samples, int absoluteStartSample)
    {
        if (!UseMmsstvFullRxForRgb || !IsMmsstvFullRxProfile(Profile) || samples.Length <= 0)
        {
            return;
        }

        if (_liveReceiveBufferAppendCursor < 0)
        {
            _liveReceiveBufferAppendCursor = StartSample;
        }

        var absoluteEndSample = absoluteStartSample + samples.Length;
        if (absoluteStartSample > _liveReceiveBufferAppendCursor)
        {
            _receiveBuffer.InsertLostSamples(absoluteStartSample - _liveReceiveBufferAppendCursor);
        }

        var appendStartSample = Math.Max(_liveReceiveBufferAppendCursor, Math.Max(StartSample, absoluteStartSample));
        if (appendStartSample >= absoluteEndSample)
        {
            return;
        }

        var localStart = appendStartSample - absoluteStartSample;
        var appendCount = absoluteEndSample - appendStartSample;
        _receiveBuffer.Append(samples.Slice(localStart, appendCount));
        _liveReceiveBufferAppendCursor = absoluteEndSample;
    }

    private (int UpdatedLines, string? Status) DrainMmsstvFullRx()
    {
        var updated = 0;
        string? status = null;

        while (_demodState.WriteLine < Profile.Height
            && _receiveBuffer.TryReadPage(out var page))
        {
            CopyMmsstvStagePage(page);
            var drawResult = DrawMmsstvBufferedPage(page);
            updated += drawResult.UpdatedPixels;
            if (drawResult.PagesDrawn == 0)
            {
                continue;
            }

            _fullRxPageIndex = Math.Max(_fullRxPageIndex, drawResult.LastLineIndex + 1);
            var targetWriteLine = Math.Max(_demodState.WriteLine, Math.Min(Profile.Height, drawResult.ProgressRows));
            _demodState.CommitDecodedRows(Profile.Width, targetWriteLine - _demodState.WriteLine);
            updated += FlushStagedRows();
            NextLineStart = Math.Max(NextLineStart, StartSample + (_fullRxPageIndex * _geometry.LineSamples));
            status = UpdateSaveStatus();
            if (Completed)
            {
                break;
            }
        }

        return (updated, status);
    }

    private static bool IsMmsstvFullRxProfile(SstvModeProfile profile)
        => profile.Family is "martin" or "scottie" or "robot36" or "robot" or "pd";

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

            var lineSegment = samples.AsSpan(lineStart, lineSamples);
            var lineError = lineStart - expectedStart;
            if (Profile.Family != "avt")
            {
                StageSyncRow(lineSegment);
            }

            StageRgbRow(targetLine, DecodeMmsstvRgbLine(lineSegment, targetLine));
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
        var lineSamples = _geometry.LineSamples;
        var offsetSamples = _geometry.OffsetSamples;
        var yScanSamples = _geometry.ScanSamples;
        var selectorStartSamples = _geometry.ChromaSelectStartSamples;
        var selectorSamples = Math.Max(1, _geometry.ChromaGreenEndSamples - selectorStartSamples);
        var chromaStartSamples = _geometry.BlueStartSamples;
        var chromaScanSamples = _geometry.Scan2Samples;

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
            var yStart = lineStart + offsetSamples;
            var selectorStart = lineStart + offsetSamples + selectorStartSamples;
            var cStart = lineStart + offsetSamples + chromaStartSamples;
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
            var selector = ResolveRobotSelector(samples.AsSpan(selectorStart, selectorSamples), _mmsstvDrawSelector);
            _mmsstvDrawSelector = selector;
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

        return _geometry.SyncStartSamples;
    }

    private IEnumerable<(string Color, int StartPos)> ChannelLayout(int lineStart, int syncSamples, int gapSamples, int scanSamples)
    {
        if (Profile.Family == "scottie")
        {
            yield return ("g", lineStart + _geometry.GreenStartSamples);
            yield return ("b", lineStart + _geometry.BlueStartSamples);
            yield return ("r", lineStart + _geometry.RedStartSamples);
            yield break;
        }

        var firstStart = lineStart + syncSamples + gapSamples;
        var secondStart = firstStart + scanSamples + gapSamples;
        var thirdStart = secondStart + scanSamples + gapSamples;
        yield return ("g", firstStart);
        yield return ("b", secondStart);
        yield return ("r", thirdStart);
    }

    private byte[] DecodeMmsstvRgbLine(ReadOnlySpan<float> lineSegment, int lineIndex)
    {
        var row = new byte[Profile.Width * 3];
        if (lineSegment.Length < 8)
        {
            return row;
        }

        var demod = DemodulateLegacyFallbackChannel(lineSegment);
        if (demod.Length == 0)
        {
            return row;
        }

        var red = new byte[Profile.Width];
        var green = new byte[Profile.Width];
        var blue = new byte[Profile.Width];

        if (Profile.Family == "scottie")
        {
            DecodeMmsstvRgbSegment(demod, green, _geometry.GreenStartSamples, usePictureLevel: Profile.Id != SstvModeId.ScottieDx);
            DecodeMmsstvRgbSegment(demod, blue, _geometry.BlueStartSamples, usePictureLevel: Profile.Id != SstvModeId.ScottieDx);
            DecodeMmsstvRgbSegment(demod, red, _geometry.RedStartSamples, usePictureLevel: Profile.Id != SstvModeId.ScottieDx);
        }
        else
        {
            DecodeMmsstvRgbSegment(demod, green, _geometry.GreenStartSamples, usePictureLevel: true);
            DecodeMmsstvRgbSegment(demod, blue, _geometry.BlueStartSamples, usePictureLevel: true);
            DecodeMmsstvRgbSegment(demod, red, _geometry.RedStartSamples, usePictureLevel: true);
        }

        red = DestripePixels(MedianFilter3(SmoothPixels(red)));
        green = DestripePixels(MedianFilter3(SmoothPixels(green)));
        blue = DestripePixels(MedianFilter3(SmoothPixels(blue)));

        for (var x = 0; x < Profile.Width; x++)
        {
            var offset = x * 3;
            row[offset] = red[x];
            row[offset + 1] = green[x];
            row[offset + 2] = blue[x];
        }

        if (_firstChannelDebug is null && lineIndex == 0)
        {
            _firstChannelDebug =
                $"mmsstv-raster: demod {demod.Min():0.000}-{demod.Max():0.000} | row {row.Min()}-{row.Max()} | kss {_geometry.ScanSamplesAdjusted}";
        }

        return row;
    }

    private byte[] DecodeMmsstvRgbLine(ReadOnlySpan<short> pictureLine, int lineIndex)
    {
        var row = new byte[Profile.Width * 3];
        if (pictureLine.Length < 8)
        {
            return row;
        }

        var red = new byte[Profile.Width];
        var green = new byte[Profile.Width];
        var blue = new byte[Profile.Width];

        if (Profile.Family == "scottie")
        {
            DecodeMmsstvRgbSegment(pictureLine, green, _geometry.GreenStartSamples, usePictureLevel: Profile.Id != SstvModeId.ScottieDx);
            DecodeMmsstvRgbSegment(pictureLine, blue, _geometry.BlueStartSamples, usePictureLevel: Profile.Id != SstvModeId.ScottieDx);
            DecodeMmsstvRgbSegment(pictureLine, red, _geometry.RedStartSamples, usePictureLevel: Profile.Id != SstvModeId.ScottieDx);
        }
        else
        {
            DecodeMmsstvRgbSegment(pictureLine, green, _geometry.GreenStartSamples, usePictureLevel: true);
            DecodeMmsstvRgbSegment(pictureLine, blue, _geometry.BlueStartSamples, usePictureLevel: true);
            DecodeMmsstvRgbSegment(pictureLine, red, _geometry.RedStartSamples, usePictureLevel: true);
        }

        for (var x = 0; x < Profile.Width; x++)
        {
            var offset = x * 3;
            row[offset] = red[x];
            row[offset + 1] = green[x];
            row[offset + 2] = blue[x];
        }

        if (_firstChannelDebug is null && lineIndex == 0)
        {
            var snapshot = _receiveBuffer?.Debug;
            var (minPicture, maxPicture) = MinMax(pictureLine);
            _firstChannelDebug =
                $"mmsstv-buffer: pic {minPicture}-{maxPicture} | row {row.Min()}-{row.Max()} | kss {_geometry.ScanSamplesAdjusted}" +
                (snapshot is null
                    ? string.Empty
                    : $" | filt {snapshot.Value.MinFiltered:0}-{snapshot.Value.MaxFiltered:0} raw {snapshot.Value.MinRaw:0}-{snapshot.Value.MaxRaw:0} lost {snapshot.Value.LostSamplesInserted} skip {snapshot.Value.SkippedSamplesConsumed}/{snapshot.Value.PendingSkipSamples}");
        }

        return row;
    }

    private MmsstvDrawResult DrawMmsstvBufferedPage(MmsstvReceiveBuffer.ReceivePage page)
    {
        if (!_mmsstvDrawBaseInitialized)
        {
            _pendingMmsstvPages.Add(page);
            if (!TryInitializeMmsstvDrawBase())
            {
                return new MmsstvDrawResult(0, 0, page.LineIndex, 0);
            }

            var updated = 0;
            var lastLineIndex = page.LineIndex;
            var progressRows = 0;
            foreach (var pendingPage in _pendingMmsstvPages)
            {
                var drawResult = DrawMmsstvBufferedPageCore(pendingPage);
                updated += drawResult.UpdatedPixels;
                progressRows = Math.Max(progressRows, drawResult.ProgressRows);
                lastLineIndex = pendingPage.LineIndex;
            }

            var pagesDrawn = _pendingMmsstvPages.Count;
            _pendingMmsstvPages.Clear();
            return new MmsstvDrawResult(updated, pagesDrawn, lastLineIndex, progressRows);
        }

        var result = DrawMmsstvBufferedPageCore(page);
        return new MmsstvDrawResult(result.UpdatedPixels, 1, page.LineIndex, result.ProgressRows);
    }

    private bool TryInitializeMmsstvDrawBase()
    {
        if (_mmsstvDrawBaseInitialized)
        {
            return true;
        }

        if (!MmsstvSyncBufferAligner.TryComputeBaseOffset(
                MmsstvStagedSyncRows,
                Profile,
                WorkingSampleRate,
                _geometry.LineSamples,
                _geometry.DrawLineSamples,
                _geometry.DrawOffsetPreviewSamples,
                MmsstvStagedSyncRows.Count,
                useRxBuffer: true,
                highAccuracy: true,
                hillTapQuarter: MmsstvHilbertDelayQuarter(),
                out var baseOffset))
        {
            return false;
        }

        _mmsstvDrawBase = baseOffset;
        _mmsstvDrawStartBase = baseOffset;
        _mmsstvDrawBaseInitialized = true;
        _mmsstvDrawAxisX = -1;
        _mmsstvDrawSelector = 0;
        return true;
    }

    private bool TryApplyMmsstvSlantCorrection(bool force = false)
    {
        if (_mmsstvSlantCorrectionApplied
            || (!force && !UseMmsstvSlantCorrection)
            || Profile.Id == SstvModeId.Avt90
            || !MmsstvSyncBufferAligner.TryComputeSlantCorrection(
                MmsstvStagedSyncRows,
                WorkingSampleRate,
                _geometry.LineSamples,
                _geometry.DrawLineSamples,
                out var correctedDrawLineSamples,
                out _mmsstvSlantDebug))
        {
            return false;
        }

        _mmsstvDrawLineSamples = correctedDrawLineSamples;
        _mmsstvDrawScale = correctedDrawLineSamples / _geometry.DrawLineSamples;
        if (TryComputeMmsstvDrawBase(correctedDrawLineSamples, out var correctedBase))
        {
            _mmsstvDrawBase = correctedBase;
            _mmsstvDrawStartBase = correctedBase;
        }

        _mmsstvSlantCorrectionApplied = true;
        _mmsstvSlantDebug = $"{_mmsstvSlantDebug} | replay";
        return true;
    }

    private MmsstvDrawPageResult DrawMmsstvBufferedPageCore(MmsstvReceiveBuffer.ReceivePage page)
        => DrawMmsstvBufferedRow(page.Picture, page.Sync, page.LineIndex, page.PageIndex, page.BaseIndex);

    private MmsstvDrawPageResult DrawMmsstvBufferedRow(
        ReadOnlySpan<short> pictureLine,
        ReadOnlySpan<short> syncLine,
        int lineIndex,
        int pageIndex,
        int baseIndex,
        int? lineWidthSamplesOverride = null,
        bool trackLiveSync = true)
    {
        var touchedRows = new bool[Profile.Height];
        var touchedCount = 0;
        var progressRows = 0;
        var lineWidth = lineWidthSamplesOverride ?? _geometry.LineSamples;

        var drawLineWidth = _mmsstvDrawLineSamples;
        var basePosition = _mmsstvDrawBase;
        var sampleCount = Math.Min(lineWidth, pictureLine.Length);

        for (var i = 0; i < sampleCount; i++)
        {
            var n = basePosition + i;
            if (n < 0)
            {
                continue;
            }

            var ps = UseMmsstvDoubleDrawClock
                ? n - (Math.Floor(n / drawLineWidth) * drawLineWidth)
                : n % lineWidth;
            var y = UseMmsstvDoubleDrawClock
                ? (int)(n / drawLineWidth)
                : n / lineWidth;
            if (trackLiveSync && i < syncLine.Length)
            {
                TrackMmsstvLiveSync(ps, syncLine[i]);
            }

            var offset = UseMmsstvDoubleDrawClock
                ? DrawOffsetSamples
                : _geometry.OffsetSamples;
            if (ps < offset)
            {
                continue;
            }

            ps -= offset;
            if (Profile.Family == "scottie")
            {
                DrawMmsstvScottieSample(pictureLine, i, ps, y, touchedRows, ref touchedCount, ref progressRows);
            }
            else if (Profile.Family == "martin")
            {
                DrawMmsstvMartinSample(pictureLine, i, ps, y, touchedRows, ref touchedCount, ref progressRows);
            }
            else if (Profile.Family == "robot36")
            {
                DrawMmsstvRobot36Sample(pictureLine, i, ps, y, touchedRows, ref touchedCount, ref progressRows);
            }
            else if (Profile.Family == "robot")
            {
                DrawMmsstvRobotSample(pictureLine, i, ps, y, touchedRows, ref touchedCount, ref progressRows);
            }
            else if (Profile.Family == "pd")
            {
                DrawMmsstvPdSample(pictureLine, i, ps, y, touchedRows, ref touchedCount, ref progressRows);
            }
        }

        _mmsstvDrawBase = basePosition + lineWidth;

        for (var rowIndex = 0; rowIndex < touchedRows.Length; rowIndex++)
        {
            if (!touchedRows[rowIndex] || _rawRows[rowIndex] is not { } row)
            {
                continue;
            }

            WriteRgbRow(rowIndex, row);
        }

        if (_firstChannelDebug is null && lineIndex == 0)
        {
            var snapshot = _receiveBuffer?.Debug;
            var (minPicture, maxPicture) = MinMax(pictureLine);
            _firstChannelDebug =
                $"mmsstv-draw: base {basePosition} rbase {baseIndex} page {pageIndex} pic {minPicture}-{maxPicture} | touched {touchedCount} | kss {_geometry.ScanSamplesAdjusted}" +
                (snapshot is null
                    ? string.Empty
                    : $" | filt {snapshot.Value.MinFiltered:0}-{snapshot.Value.MaxFiltered:0} raw {snapshot.Value.MinRaw:0}-{snapshot.Value.MaxRaw:0} lost {snapshot.Value.LostSamplesInserted} skip {snapshot.Value.SkippedSamplesConsumed}/{snapshot.Value.PendingSkipSamples}");
        }

        return new MmsstvDrawPageResult(touchedCount, progressRows);
    }

    private void TrackMmsstvLiveSync(double ps, short syncValue)
    {
        var syncPosition = (int)ps;
        if (syncPosition == 0)
        {
            if (_mmsstvSyncPos != -1)
            {
                RunMmsstvAutoSyncJob();
            }

            _mmsstvSyncMin = syncValue;
            _mmsstvSyncMax = syncValue;
            _mmsstvSyncRPos = _mmsstvSyncPos;
            return;
        }

        if (_mmsstvSyncMax < syncValue)
        {
            _mmsstvSyncMax = syncValue;
            _mmsstvSyncPos = syncPosition;
        }
        else if (_mmsstvSyncMin > syncValue)
        {
            _mmsstvSyncMin = syncValue;
        }
    }

    private void RunMmsstvAutoSyncJob()
    {
        if (Profile.Id == SstvModeId.Avt90)
        {
            return;
        }

        var lineWidth = Math.Max(1, (int)_mmsstvDrawLineSamples);
        var mult = Math.Max(1, lineWidth / 320);
        var autoSyncDiff = Math.Min(mult * 3, 45 * WorkingSampleRate / 11025);
        var offsetPreview = (int)(_geometry.DrawOffsetPreviewSamples * _mmsstvDrawScale);
        var autoSlantSyncThreshold = 5 * mult;
        _mmsstvAutoStopPos = _mmsstvSyncPos - offsetPreview;
        var halfWidth = lineWidth / 2;
        if (_mmsstvAutoStopPos > halfWidth)
        {
            _mmsstvAutoStopPos -= lineWidth;
        }

        if (_mmsstvAutoStopAverageCount >= 8)
        {
            var nearCount = 0;
            for (var i = 0; i < _mmsstvAutoStopPositions.Length; i++)
            {
                var delta = Math.Abs(_mmsstvAutoStopPos - _mmsstvAutoStopPositions[i]);
                if (_mmsstvAutoStopAverageCount >= 16)
                {
                    if (delta <= 14 * mult)
                    {
                        nearCount++;
                    }
                }
                else if (delta <= 10 * mult)
                {
                    nearCount++;
                }
            }

            var syncSpan = _mmsstvSyncMax - _mmsstvSyncMin;
            if (nearCount < 4)
            {
                if (_mmsstvAutoSync
                    && nearCount >= 2
                    && _mmsstvAutoSyncPos != MmsstvSyncUnset
                    && _mmsstvAutoSyncCount == 0
                    && syncSpan > 5000)
                {
                    var delta = _mmsstvAutoStopPos - _mmsstvAutoStopPositions[15];
                    if (Math.Abs(delta) <= autoSlantSyncThreshold)
                    {
                        delta = _mmsstvAutoStopPos - _mmsstvAutoSyncPos;
                        if (Math.Abs(delta) >= autoSlantSyncThreshold)
                        {
                            ApplyMmsstvAutoSyncSkip(_mmsstvAutoStopPos, lineWidth);
                        }
                    }
                }

                if (_mmsstvAutoStop && (nearCount < 2 || syncSpan < 8192))
                {
                    _mmsstvAutoStopCount++;
                }
            }
            else
            {
                _mmsstvAutoSyncPos = _mmsstvAutoStopPos;
                _mmsstvAutoStopCount = Math.Max(0, _mmsstvAutoStopCount - 2);
            }

            if (_mmsstvAutoSync
                && _mmsstvAutoSyncDisable == 0
                && _mmsstvAutoSyncPos != MmsstvSyncUnset
                && _mmsstvAutoSyncCount > 0
                && syncSpan > 5000)
            {
                var delta = _mmsstvAutoStopPos - _mmsstvAutoStopPositions[15];
                if (Math.Abs(delta) <= autoSyncDiff
                    && Math.Abs(_mmsstvAutoStopPos) >= autoSyncDiff)
                {
                    ApplyMmsstvAutoSyncSkip(_mmsstvAutoStopPos, lineWidth);
                }
            }
        }

        RunMmsstvAutoSlantJob(mult, lineWidth);

        if (_mmsstvAutoSyncDisable > 0)
        {
            _mmsstvAutoSyncDisable--;
        }

        _mmsstvAutoStopAverageCount++;
        Array.Copy(_mmsstvAutoStopPositions, 1, _mmsstvAutoStopPositions, 0, _mmsstvAutoStopPositions.Length - 1);
        _mmsstvAutoStopPositions[^1] = _mmsstvAutoStopPos;
        _mmsstvAutoSlantY++;
    }

    private void RunMmsstvAutoSlantJob(int mult, int lineWidth)
    {
        if (_mmsstvAutoSyncCount != 0 || _mmsstvAutoStopAverageCount < 5)
        {
            return;
        }

        var maxDelta = Math.Abs(_mmsstvAutoStopPositions[15] - _mmsstvAutoStopPositions[14]);
        maxDelta = Math.Max(maxDelta, Math.Abs(_mmsstvAutoStopPositions[14] - _mmsstvAutoStopPositions[13]));
        maxDelta = Math.Max(maxDelta, Math.Abs(_mmsstvAutoStopPositions[13] - _mmsstvAutoStopPositions[12]));
        maxDelta = Math.Max(maxDelta, Math.Abs(_mmsstvAutoStopPositions[12] - _mmsstvAutoStopPositions[11]));
        maxDelta = Math.Max(maxDelta, Math.Abs(_mmsstvAutoStopPositions[11] - _mmsstvAutoStopPositions[10]));
        if (maxDelta >= 8 * mult)
        {
            return;
        }

        var position = GetMmsstvLeastSquaresAutoStopPosition(5);
        if (_mmsstvAutoSlantBeginPos == MmsstvSyncUnset)
        {
            _mmsstvAutoSlantBeginPos = position;
            _mmsstvAutoSlantY = 0;
            return;
        }

        if (_mmsstvAutoSlantY < 3)
        {
            return;
        }

        var driftSamplesPerLine = (_mmsstvAutoSlantBeginPos - position) * WorkingSampleRate / (double)lineWidth;
        driftSamplesPerLine /= _mmsstvAutoSlantY;
        var smoothedSampleRate = _mmsstvAutoSlantAverage.Average(WorkingSampleRate - driftSamplesPerLine);
        var correction = WorkingSampleRate - smoothedSampleRate;
        if (!ShouldApplyMmsstvAutoSlant(correction))
        {
            return;
        }

        if (_mmsstvAutoSlantY >= 32)
        {
            _mmsstvAutoSlantBitMask |= 1;
        }

        if (_mmsstvAutoSlantY >= _mmsstvAutoSlantPositions[0])
        {
            _mmsstvAutoSlantBitMask |= 2;
        }

        if (_mmsstvAutoSlantY >= _mmsstvAutoSlantPositions[1])
        {
            _mmsstvAutoSlantBitMask |= 4;
        }

        if (_mmsstvAutoSlantY >= _mmsstvAutoSlantPositions[2])
        {
            _mmsstvAutoSlantBitMask |= 8;
        }

        if (_mmsstvAutoSlantY >= _mmsstvAutoSlantPositions[3])
        {
            _mmsstvAutoSlantBitMask |= 16;
        }

        if (_mmsstvAutoSlantDisable != 0)
        {
            return;
        }

        var maxSampleRate = WorkingSampleRate * 1100.0 / 1060.0;
        if (smoothedSampleRate > maxSampleRate)
        {
            smoothedSampleRate = maxSampleRate;
        }

        var normalizedSampleRate = NormalizeMmsstvSampleFrequency(smoothedSampleRate, 50.0);
        ApplyMmsstvAutoSlantSampleRate(normalizedSampleRate, position, correction);
    }

    private bool ShouldApplyMmsstvAutoSlant(double correction)
    {
        var magnitude = Math.Abs(correction);
        return ((_mmsstvAutoSlantBitMask & 1) == 0 && magnitude >= _mmsstvAutoSlantLimits[0])
            || ((_mmsstvAutoSlantBitMask & 1) == 0 && _mmsstvAutoSlantY >= 16 && magnitude >= _mmsstvAutoSlantLimits[1])
            || ((_mmsstvAutoSlantBitMask & 1) == 0 && _mmsstvAutoSlantY >= 32 && magnitude >= _mmsstvAutoSlantLimits[2])
            || ((_mmsstvAutoSlantBitMask & 2) == 0 && _mmsstvAutoSlantY >= _mmsstvAutoSlantPositions[0] && magnitude >= _mmsstvAutoSlantLimits[3])
            || ((_mmsstvAutoSlantBitMask & 4) == 0 && _mmsstvAutoSlantY >= _mmsstvAutoSlantPositions[1] && magnitude >= _mmsstvAutoSlantLimits[4])
            || ((_mmsstvAutoSlantBitMask & 8) == 0 && _mmsstvAutoSlantY >= _mmsstvAutoSlantPositions[2] && magnitude >= _mmsstvAutoSlantLimits[5])
            || ((_mmsstvAutoSlantBitMask & 16) == 0 && _mmsstvAutoSlantY >= _mmsstvAutoSlantPositions[3] && magnitude >= _mmsstvAutoSlantLimits[6]);
    }

    private void ApplyMmsstvAutoSlantSampleRate(double sampleRate, double position, double correction)
    {
        var nextDrawLineSamples = _geometry.DrawLineSamples * sampleRate / WorkingSampleRate;
        if (nextDrawLineSamples <= 0.0 || !double.IsFinite(nextDrawLineSamples))
        {
            return;
        }

        _mmsstvDrawLineSamples = nextDrawLineSamples;
        _mmsstvDrawScale = nextDrawLineSamples / _geometry.DrawLineSamples;
        if (TryComputeMmsstvDrawBase(nextDrawLineSamples, out var baseOffset))
        {
            _mmsstvDrawBase = baseOffset;
            _mmsstvDrawStartBase = baseOffset;
        }

        _mmsstvSlantDebug =
            $"auto-slant: y {_mmsstvAutoSlantY} pos {position} fq {sampleRate:0.00} tw {nextDrawLineSamples:0.000} d {correction:0.000}";
    }

    private double GetMmsstvLeastSquaresAutoStopPosition(int count)
    {
        double t = 0.0;
        double l = 0.0;
        double tt = 0.0;
        double tl = 0.0;
        for (var i = 0; i < count; i++)
        {
            t += i;
            var value = _mmsstvAutoStopPositions[15 - i];
            l += value;
            tt += i * i;
            tl += i * value;
        }

        return ((l * tt) - (t * tl)) / ((count * tt) - (t * t));
    }

    private void ApplyMmsstvAutoSyncSkip(int skip, int lineWidth)
    {
        if (skip < 0)
        {
            skip += lineWidth;
        }

        _receiveBuffer.SetSkipSamples(skip);
        _mmsstvSyncPos = -1;
        _mmsstvSyncRPos = -1;
        _mmsstvAutoSyncPos = MmsstvSyncUnset;
        _mmsstvAutoSyncDisable = 6;
        _mmsstvAutoSyncCount++;
        if (_mmsstvAutoStopCount > 0)
        {
            _mmsstvAutoStopCount--;
        }
    }

    private MmsstvDrawPageResult ReplayMmsstvStagedRows()
    {
        if (_mmsstvStagedPictureRows.Count == 0)
        {
            return new MmsstvDrawPageResult(0, 0);
        }

        ClearMmsstvDrawSurface();
        ResetMmsstvDrawReplayState();
        var updated = 0;
        var progressRows = 0;
        for (var i = 0; i < _mmsstvStagedPictureRows.Count; i++)
        {
            var syncLine = i < _mmsstvStagedSyncRows.Count
                ? _mmsstvStagedSyncRows[i]
                : ReadOnlySpan<short>.Empty;
            var result = DrawMmsstvBufferedRow(
                _mmsstvStagedPictureRows[i],
                syncLine,
                i,
                i % MmsstvDemodState.DemodBufferMax,
                i * _geometry.LineSamples,
                trackLiveSync: false);
            updated += result.UpdatedPixels;
            progressRows = Math.Max(progressRows, result.ProgressRows);
        }

        return new MmsstvDrawPageResult(updated, progressRows);
    }

    public bool ApplyMmsstvPostReceiveSlantCorrection(bool force = false)
    {
        if (!UsesMmsstvBufferedDraw || !TryApplyMmsstvSlantCorrection(force))
        {
            return false;
        }

        ReplayMmsstvStagedRows();
        SaveImage();
        return true;
    }

    public bool ApplyMmsstvPostReceiveSyncAdjustment()
    {
        if (!UsesMmsstvBufferedDraw
            || Profile.Id == SstvModeId.Avt90
            || _mmsstvStagedPictureRows.Count == 0
            || MmsstvStagedSyncRows.Count == 0
            || !TryComputeMmsstvDrawBase(_mmsstvDrawLineSamples, out var baseOffset))
        {
            _mmsstvSyncAdjustDebug = $"sync-adjust: skipped rows {_mmsstvStagedPictureRows.Count}/{MmsstvStagedSyncRows.Count}";
            return false;
        }

        _mmsstvDrawBase = baseOffset;
        _mmsstvDrawStartBase = baseOffset;
        ClearMmsstvDrawSurface();
        ResetMmsstvDrawReplayState();

        var threshold = MmsstvSyncAdjustThresholdSamples();
        var adjustments = 0;
        var largestOffset = 0;
        var progressRows = 0;
        var lineCount = Math.Min(_mmsstvStagedPictureRows.Count, MmsstvStagedSyncRows.Count);
        for (var i = 0; i < lineCount; i++)
        {
            var lineBase = _mmsstvDrawBase;
            var syncOffset = ComputeMmsstvLineSyncAdjustment(MmsstvStagedSyncRows[i], lineBase);
            var result = DrawMmsstvBufferedRow(
                _mmsstvStagedPictureRows[i],
                MmsstvStagedSyncRows[i],
                i,
                i % MmsstvDemodState.DemodBufferMax,
                i * _geometry.LineSamples,
                trackLiveSync: false);
            progressRows = Math.Max(progressRows, result.ProgressRows);
            largestOffset = Math.Max(largestOffset, Math.Abs(syncOffset));
            if (progressRows >= 2 && Math.Abs(syncOffset) > threshold)
            {
                _mmsstvDrawBase += syncOffset;
                adjustments++;
            }
        }

        SaveImage();
        _mmsstvSyncAdjustDebug =
            $"sync-adjust: rows {lineCount} threshold {threshold} adjusted {adjustments} max {largestOffset}";
        return adjustments > 0;
    }

    private void ResetMmsstvDrawReplayState()
    {
        _mmsstvDrawBase = _mmsstvDrawStartBase;
        _mmsstvDrawAxisX = -1;
        _mmsstvDrawSelector = 0;
        Array.Clear(_mmsstvDrawLuma);
        Array.Clear(_mmsstvDrawChannel0);
        Array.Clear(_mmsstvDrawChannel1);
    }

    private bool TryComputeMmsstvDrawBase(double drawLineSamples, out int baseOffset)
    {
        var offsetPreviewSamples = _geometry.DrawOffsetPreviewSamples * drawLineSamples / _geometry.DrawLineSamples;
        if (!MmsstvSyncBufferAligner.TryComputeBaseOffset(
                MmsstvStagedSyncRows,
                Profile,
                WorkingSampleRate,
                _geometry.LineSamples,
                drawLineSamples,
                offsetPreviewSamples,
                MmsstvStagedSyncRows.Count,
                useRxBuffer: true,
                highAccuracy: true,
                hillTapQuarter: MmsstvHilbertDelayQuarter(),
                out baseOffset))
        {
            return false;
        }

        return true;
    }

    private int ComputeMmsstvLineSyncAdjustment(short[] syncLine, int lineBase)
    {
        if (lineBase < 0 || syncLine.Length == 0)
        {
            return 0;
        }

        var max = short.MinValue;
        var rawPosition = 0;
        var sampleCount = Math.Min(_geometry.LineSamples, syncLine.Length);
        for (var i = 0; i < sampleCount; i++)
        {
            var value = syncLine[i];
            if (value <= max)
            {
                continue;
            }

            max = value;
            rawPosition = Wrap(lineBase + i, _mmsstvDrawLineSamples);
        }

        var adjusted = MmsstvSyncPositionAdjuster.Adjust(
            rawSyncPosition: rawPosition,
            profile: Profile,
            sampleRate: WorkingSampleRate,
            offsetPreviewSamples: _geometry.DrawOffsetPreviewSamples * _mmsstvDrawLineSamples / _geometry.DrawLineSamples,
            lineWidthSamples: _geometry.LineSamples,
            stageLineCount: MmsstvStagedSyncRows.Count,
            hillTapQuarter: MmsstvHilbertDelayQuarter());

        var halfLine = (int)_mmsstvDrawLineSamples / 2;
        if (adjusted >= halfLine)
        {
            adjusted -= (int)_mmsstvDrawLineSamples;
        }

        return adjusted;
    }

    private int MmsstvSyncAdjustThresholdSamples()
    {
        var k = Profile.Id switch
        {
            SstvModeId.Pd50 or SstvModeId.Pd120 => 3.0,
            SstvModeId.Pd160 => 2.0,
            SstvModeId.Robot24 or SstvModeId.Pd90 or SstvModeId.Scottie2 or SstvModeId.Martin2 => 1.0,
            _ => 0.5
        };

        return Math.Max(1, (int)((_geometry.DrawScanSamples * k / Profile.Width) + 0.5));
    }

    private static int Wrap(int value, double width)
        => (int)(value - (Math.Floor(value / width) * width));

    private void ClearMmsstvDrawSurface()
    {
        Array.Clear(_rgb);
        Array.Clear(_rawRows);
        Array.Clear(_rawAuxRows);
        Array.Clear(_stagedRgbRows);
        Array.Clear(_stagedLineNumbers);
        Array.Clear(_stagedRowOccupied);
    }

    private void DrawMmsstvScottieSample(
        ReadOnlySpan<short> pictureLine,
        int sampleIndex,
        double ps,
        int y,
        bool[] touchedRows,
        ref int touchedCount,
        ref int progressRows)
    {
        var usePictureLevel = Profile.Id != SstvModeId.ScottieDx;
        if (ps < DrawScanSamples)
        {
            var x = PictureXFromDrawSample(ps);
            if (!AcceptMmsstvDrawX(x))
            {
                return;
            }

            var red = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel);
            WriteBufferedPixel(y - 1, x, red, _mmsstvDrawChannel0[x], _mmsstvDrawChannel1[x], touchedRows, ref touchedCount, ref progressRows);
            return;
        }

        var greenPs = ps - DrawChromaGreenStartSamples;
        if (greenPs >= 0 && ps < DrawChromaGreenEndSamples)
        {
            var x = PictureXFromDrawSample(greenPs);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawChannel0[x] = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel);
            }

            return;
        }

        var blueStart = DrawChromaBlueStartSamples;
        var bluePs = ps - blueStart;
        if (bluePs >= 0 && ps < DrawChromaBlueEndSamples)
        {
            var x = PictureXFromDrawSample(bluePs);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawChannel1[x] = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel);
            }
        }
    }

    private void DrawMmsstvMartinSample(
        ReadOnlySpan<short> pictureLine,
        int sampleIndex,
        double ps,
        int y,
        bool[] touchedRows,
        ref int touchedCount,
        ref int progressRows)
    {
        if (ps < DrawScanSamples)
        {
            var x = PictureXFromDrawSample(ps);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawChannel0[x] = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel: true);
            }

            return;
        }

        var secondPs = ps - DrawChromaGreenStartSamples;
        if (secondPs >= 0 && ps < DrawChromaGreenEndSamples)
        {
            var x = PictureXFromDrawSample(secondPs);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawChannel1[x] = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel: true);
            }

            return;
        }

        var thirdStart = DrawChromaBlueStartSamples;
        var thirdPs = ps - thirdStart;
        if (thirdPs >= 0 && ps < DrawChromaBlueEndSamples)
        {
            var x = PictureXFromDrawSample(thirdPs);
            if (!AcceptMmsstvDrawX(x))
            {
                return;
            }

            var red = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel: true);
            WriteBufferedPixel(y, x, red, _mmsstvDrawChannel0[x], _mmsstvDrawChannel1[x], touchedRows, ref touchedCount, ref progressRows);
        }
    }

    private void DrawMmsstvRobot36Sample(
        ReadOnlySpan<short> pictureLine,
        int sampleIndex,
        double ps,
        int y,
        bool[] touchedRows,
        ref int touchedCount,
        ref int progressRows)
    {
        if (ps < DrawScanSamples)
        {
            var x = PictureXFromDrawSample(ps);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawLuma[x] = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel: true);
            }

            return;
        }

        if (ps < DrawChromaGreenEndSamples)
        {
            var selectorPs = ps - DrawChromaGreenStartSamples;
            if (selectorPs >= 0)
            {
                var selector = GetMmsstvPixelLevel(pictureLine[sampleIndex]);
                if (selector >= 64 || selector < -64)
                {
                    _mmsstvDrawSelector = selector >= 0 ? 1 : 0;
                }
                else
                {
                    _mmsstvDrawSelector = _mmsstvDrawSelector == 0 ? 1 : 0;
                }
            }

            return;
        }

        if (ps < DrawChromaBlueEndSamples)
        {
            var chromaPs = ps - DrawChromaBlueStartSamples;
            var x = PictureXFromDrawSample2(chromaPs);
            if (AcceptMmsstvDrawX(x) && y < Profile.Height)
            {
                if (_mmsstvDrawSelector == 0)
                {
                    _mmsstvDrawChannel0[x] = GetMmsstvPixelLevel(pictureLine[sampleIndex]);
                }
                else
                {
                    _mmsstvDrawChannel1[x] = GetMmsstvPixelLevel(pictureLine[sampleIndex]);
                }

                var (red, green, blue) = MmsstvYcToRgb(_mmsstvDrawLuma[x], _mmsstvDrawChannel0[x], _mmsstvDrawChannel1[x]);
                WriteBufferedPixel(y, x, red, green, blue, touchedRows, ref touchedCount, ref progressRows);
            }
        }
    }

    private void DrawMmsstvRobotSample(
        ReadOnlySpan<short> pictureLine,
        int sampleIndex,
        double ps,
        int y,
        bool[] touchedRows,
        ref int touchedCount,
        ref int progressRows)
    {
        if (ps < DrawScanSamples)
        {
            var x = PictureXFromDrawSample(ps);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawLuma[x] = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel: true);
            }

            return;
        }

        if (ps < DrawChromaGreenEndSamples)
        {
            var ryPs = ps - DrawChromaGreenStartSamples;
            var x = PictureXFromDrawSample2(ryPs);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawChannel1[x] = GetMmsstvPixelLevel(pictureLine[sampleIndex]);
            }

            return;
        }

        if (ps < DrawChromaBlueEndSamples)
        {
            var byPs = ps - DrawChromaBlueStartSamples;
            var x = PictureXFromDrawSample2(byPs);
            if (!AcceptMmsstvDrawX(x) || y >= Profile.Height)
            {
                return;
            }

            var by = GetMmsstvPixelLevel(pictureLine[sampleIndex]);
            var (red, green, blue) = MmsstvYcToRgb(_mmsstvDrawLuma[x], _mmsstvDrawChannel1[x], by);
            if (Profile.Id == SstvModeId.Robot24)
            {
                var row0 = y * 2;
                WriteBufferedPixel(row0, x, red, green, blue, touchedRows, ref touchedCount, ref progressRows);
                WriteBufferedPixel(row0 + 1, x, red, green, blue, touchedRows, ref touchedCount, ref progressRows);
                return;
            }

            WriteBufferedPixel(y, x, red, green, blue, touchedRows, ref touchedCount, ref progressRows);
        }
    }

    private void DrawMmsstvPdSample(
        ReadOnlySpan<short> pictureLine,
        int sampleIndex,
        double ps,
        int y,
        bool[] touchedRows,
        ref int touchedCount,
        ref int progressRows)
    {
        if (ps < DrawScanSamples)
        {
            var x = PictureXFromDrawSample(ps);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawLuma[x] = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel: true);
            }

            return;
        }

        if (ps < DrawChromaGreenEndSamples)
        {
            var ryPs = ps - DrawChromaGreenStartSamples;
            var x = PictureXFromDrawSample(ryPs);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawChannel1[x] = GetMmsstvPixelLevel(pictureLine[sampleIndex]);
            }

            return;
        }

        if (ps < DrawChromaBlueEndSamples)
        {
            var byPs = ps - DrawChromaBlueStartSamples;
            var x = PictureXFromDrawSample(byPs);
            if (AcceptMmsstvDrawX(x))
            {
                _mmsstvDrawChannel0[x] = GetMmsstvPixelLevel(pictureLine[sampleIndex]);
            }

            return;
        }

        if (ps < DrawChromaBlueEndSamples + DrawScanSamples)
        {
            var yEvenPs = ps - DrawChromaBlueEndSamples;
            var x = PictureXFromDrawSample(yEvenPs);
            var row0 = y * 2;
            if (AcceptMmsstvDrawX(x) && row0 >= 0 && row0 + 1 < Profile.Height)
            {
                var (red0, green0, blue0) = MmsstvYcToRgb(_mmsstvDrawLuma[x], _mmsstvDrawChannel1[x], _mmsstvDrawChannel0[x]);
                WriteBufferedPixel(row0, x, red0, green0, blue0, touchedRows, ref touchedCount, ref progressRows);

                var y1 = GetMmsstvDrawLevel(pictureLine, sampleIndex, usePictureLevel: true);
                var (red1, green1, blue1) = MmsstvYcToRgb(y1, _mmsstvDrawChannel1[x], _mmsstvDrawChannel0[x]);
                WriteBufferedPixel(row0 + 1, x, red1, green1, blue1, touchedRows, ref touchedCount, ref progressRows);
            }
        }
    }

    private int PictureXFromSample(int ps)
        => (int)(ps * (long)Profile.Width / Math.Max(1, _geometry.ScanSamplesAdjusted));

    private int PictureXFromSample(double ps)
        => (int)(ps * Profile.Width / Math.Max(1.0, DrawScanSamplesAdjusted));

    private int PictureXFromDrawSample(double ps)
        => UseMmsstvDoubleDrawClock
            ? PictureXFromSample(ps)
            : PictureXFromSample((int)ps);

    private int PictureXFromDrawSample2(double ps)
        => UseMmsstvDoubleDrawClock
            ? (int)(ps * Profile.Width / Math.Max(1.0, DrawScan2SamplesAdjusted))
            : (int)((int)ps * (long)Profile.Width / Math.Max(1, _geometry.Scan2SamplesAdjusted));

    private double DrawOffsetSamples => _geometry.DrawOffsetSamples * _mmsstvDrawScale;
    private double DrawScanSamples => _geometry.DrawScanSamples * _mmsstvDrawScale;
    private double DrawScanSamplesAdjusted => _geometry.DrawScanSamplesAdjusted * _mmsstvDrawScale;
    private double DrawScan2SamplesAdjusted => _geometry.DrawScan2SamplesAdjusted * _mmsstvDrawScale;
    private double DrawChromaGreenStartSamples => _geometry.DrawChromaGreenStartSamples * _mmsstvDrawScale;
    private double DrawChromaGreenEndSamples => _geometry.DrawChromaGreenEndSamples * _mmsstvDrawScale;
    private double DrawChromaBlueStartSamples => _geometry.DrawChromaBlueStartSamples * _mmsstvDrawScale;
    private double DrawChromaBlueEndSamples => _geometry.DrawChromaBlueEndSamples * _mmsstvDrawScale;

    private bool AcceptMmsstvDrawX(int x)
    {
        if (x == _mmsstvDrawAxisX || x < 0 || x >= Profile.Width)
        {
            return false;
        }

        _mmsstvDrawAxisX = x;
        return true;
    }

    private int GetMmsstvDrawLevel(ReadOnlySpan<short> pictureLine, int sampleIndex, bool usePictureLevel)
    {
        var level = usePictureLevel
            ? GetMmsstvPictureLevel(pictureLine, sampleIndex, _geometry.BlackAdjustSamples)
            : GetMmsstvPixelLevel(pictureLine[sampleIndex]);
        return Math.Clamp(level + 128, 0, 255);
    }

    private void WriteBufferedPixel(
        int rowIndex,
        int x,
        int red,
        int green,
        int blue,
        bool[] touchedRows,
        ref int touchedCount,
        ref int progressRows)
    {
        if (rowIndex < 0 || rowIndex >= Profile.Height || x < 0 || x >= Profile.Width)
        {
            return;
        }

        var row = _rawRows[rowIndex] ??= new byte[Profile.Width * 3];
        var offset = x * 3;
        row[offset] = (byte)red;
        row[offset + 1] = (byte)green;
        row[offset + 2] = (byte)blue;
        if (!touchedRows[rowIndex])
        {
            touchedRows[rowIndex] = true;
            touchedCount++;
        }

        progressRows = Math.Max(progressRows, rowIndex + 1);
    }

    private void DecodeMmsstvRgbSegment(double[] demod, byte[] target, int segmentStartSamples, bool usePictureLevel)
    {
        var effectiveScanSamples = Math.Clamp(_geometry.ScanSamplesAdjusted, 1, demod.Length);
        var blackAdjustSamples = _geometry.BlackAdjustSamples;
        for (var x = 0; x < target.Length; x++)
        {
            var position = segmentStartSamples + (((x + 0.5) * effectiveScanSamples / target.Length) - 0.5);
            if (position < 0 || position >= demod.Length)
            {
                continue;
            }

            var sample = usePictureLevel
                ? GetPictureLevel(demod, position, blackAdjustSamples)
                : SampleInterpolated(demod, position);
            target[x] = GetPicturePixelLevel(sample, _calibration);
        }
    }

    private void DecodeMmsstvRgbSegment(ReadOnlySpan<short> pictureLine, byte[] target, int segmentStartSamples, bool usePictureLevel)
    {
        var effectiveScanSamples = Math.Clamp(_geometry.ScanSamplesAdjusted, 1, pictureLine.Length);
        var blackAdjustSamples = _geometry.BlackAdjustSamples;
        for (var x = 0; x < target.Length; x++)
        {
            var position = segmentStartSamples + ((x * effectiveScanSamples) / target.Length);
            if (position < 0 || position >= pictureLine.Length)
            {
                continue;
            }

            var level = usePictureLevel
                ? GetMmsstvPictureLevel(pictureLine, position, blackAdjustSamples)
                : GetMmsstvPixelLevel(pictureLine[position]);
            target[x] = (byte)Math.Clamp(level + 128, 0, 255);
        }
    }

    private static int GetMmsstvPictureLevel(ReadOnlySpan<short> pictureLine, int position, int blackAdjustSamples)
    {
        var adjustedPosition = position + Math.Max(1, blackAdjustSamples);
        if (adjustedPosition < pictureLine.Length && pictureLine[position] < pictureLine[adjustedPosition])
        {
            return GetMmsstvPixelLevel(pictureLine[adjustedPosition]);
        }

        return GetMmsstvPixelLevel(pictureLine[position]);
    }

    private static int GetMmsstvPixelLevel(short raw)
        => (int)(raw * (128.0 / 16384.0));

    private static (int Red, int Green, int Blue) MmsstvYcToRgb(int y, int ry, int by)
    {
        var yv = y - 16.0;
        var red = Math.Clamp((int)((1.164457 * yv) + (1.596128 * ry)), 0, 255);
        var green = Math.Clamp((int)((1.164457 * yv) - (0.813022 * ry) - (0.391786 * by)), 0, 255);
        var blue = Math.Clamp((int)((1.164457 * yv) + (2.017364 * by)), 0, 255);
        return (red, green, blue);
    }

    private static (short Min, short Max) MinMax(ReadOnlySpan<short> values)
    {
        if (values.Length == 0)
        {
            return (0, 0);
        }

        var min = values[0];
        var max = values[0];
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] < min)
            {
                min = values[i];
            }

            if (values[i] > max)
            {
                max = values[i];
            }
        }

        return (min, max);
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

        var demod = DemodulateLegacyFallbackChannel(segment);
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

        var demod = DemodulateLegacyFallbackChannel(segment);
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

        var demod = DemodulateLegacyFallbackChannel(segment);
        if (demod.Length == 0)
        {
            return fallbackSelector;
        }

        var selector = fallbackSelector;
        for (var i = 0; i < demod.Length; i++)
        {
            var selectorLevel = GetSignedPixelLevel(demod[i], _calibration);
            if (selectorLevel >= 64 || selectorLevel < -64)
            {
                selector = selectorLevel >= 0 ? 1 : 0;
            }
            else
            {
                selector = selector == 0 ? 1 : 0;
            }
        }

        return selector;
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

        var demod = DemodulateLegacyFallbackChannel(segment);
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

        var demod = DemodulateLegacyFallbackChannel(segment);
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

    private double[] DemodulateLegacyFallbackChannel(ReadOnlySpan<float> segment)
        => DemodulateLegacyFallbackChannel(segment, out _);

    private double[] DemodulateLegacyFallbackChannel(ReadOnlySpan<float> segment, out double[] rawDemod)
    {
        var demodulators = new MmsstvDemodulatorBank(WorkingSampleRate, Profile.Narrow);
        var level = new MmsstvLevelTracker(WorkingSampleRate) { FastAgc = true };
        var demodType = MmsstvDemodulatorType.ZeroCrossing;

        rawDemod = new double[segment.Length];
        var demod = new double[segment.Length];
        for (var i = 0; i < segment.Length; i++)
        {
            level.Process(segment[i] * 16384.0);
            level.Fix();

            var raw = demodulators.ProcessRaw(level.Current, demodType);
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

        DemodulateLegacyFallbackChannel(segment, out var rawDemod);
        var usable = Math.Min(rawDemod.Length, auxRow.Length - offset);
        for (var i = 0; i < usable; i++)
        {
            var value = (rawDemod[i] + 16384.0) * 0.25;
            auxRow[offset + i] = (short)Math.Clamp((int)Math.Round(value), 0, short.MaxValue);
        }
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
    }

    private void StageSyncRow(short[] row)
    {
        if (row.Length <= 0)
        {
            return;
        }

        _syncRowHistory.Add(CopyMmsstvWideRow(row));
    }

    private void CopyMmsstvStagePage(MmsstvReceiveBuffer.ReceivePage page)
    {
        if (page.Picture.Length <= 0 || page.Sync.Length <= 0)
        {
            return;
        }

        // TMmsstv::CopyStgBuf copies exactly SSTVSET.m_WD shorts from the
        // current m_Buf/m_B12 page into append-only m_StgBuf/m_StgB12 rows.
        var picture = CopyMmsstvWideRow(page.Picture);
        var sync = CopyMmsstvWideRow(page.Sync);
        if (picture.Length == 0 || sync.Length == 0)
        {
            return;
        }

        _mmsstvStagedPictureRows.Add(picture);
        _mmsstvStagedSyncRows.Add(sync);
    }

    private short[] CopyMmsstvWideRow(short[] row)
    {
        var width = Math.Min(_geometry.LineSamples, row.Length);
        var copy = new short[width];
        Array.Copy(row, copy, width);
        return copy;
    }

    private void TryApplySyncBufferAlignment()
    {
        var syncRows = MmsstvStagedSyncRows;
        if (_syncBaseApplied || syncRows.Count < 4)
        {
            return;
        }

        if (!MmsstvSyncBufferAligner.TryComputeBaseOffset(
                syncRows,
                Profile,
                WorkingSampleRate,
                _geometry.LineSamples,
                _geometry.DrawLineSamples,
                _geometry.DrawOffsetPreviewSamples,
                syncRows.Count,
                useRxBuffer: true,
                highAccuracy: true,
                hillTapQuarter: MmsstvHilbertDelayQuarter(),
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

    private IReadOnlyList<short[]> MmsstvStagedSyncRows =>
        _mmsstvStagedSyncRows.Count > 0 ? _mmsstvStagedSyncRows : _syncRowHistory;

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

    private static int[] CreateMmsstvAutoSlantPositions(SstvModeProfile profile)
    {
        var positions = new[] { 64, 128, 160, Math.Max(0, profile.Height - 36) };
        if (profile.Family == "pd")
        {
            if (profile.Id is SstvModeId.Pd50 or SstvModeId.Pd90)
            {
                positions[0] = 48;
                positions[1] = 64;
                positions[2] = 72;
                positions[3] = 110;
            }
            else if (profile.Id == SstvModeId.Pd160)
            {
                positions[0] = 48;
                positions[1] = 80;
                positions[2] = 126;
                positions[3] = 160;
            }
            else if (profile.Id == SstvModeId.Pd290)
            {
                positions[3] = 240;
            }
        }

        return positions;
    }

    private static double[] CreateMmsstvAutoSlantLimits(int sampleRate)
        =>
        [
            25.0 * sampleRate / 11025.0,
            10.0 * sampleRate / 11025.0,
            2.0 * sampleRate / 11025.0,
            0.5 * sampleRate / 11025.0,
            0.2 * sampleRate / 11025.0,
            0.2 * sampleRate / 11025.0,
            0.08 * sampleRate / 11025.0,
        ];

    private static double NormalizeMmsstvSampleFrequency(double sampleRate, double step)
    {
        if (step <= 0.0)
        {
            return sampleRate;
        }

        return Math.Round(sampleRate / step, MidpointRounding.AwayFromZero) * step;
    }

    private static int MmsstvHilbertDelayQuarter()
        => new MmsstvHilbertDemodulator(WorkingSampleRate).HalfTap / 4;

    private sealed class MmsstvMovingAverage
    {
        private readonly double[] _values;
        private int _index;
        private int _count;

        public MmsstvMovingAverage(int count)
        {
            _values = new double[Math.Max(1, count)];
        }

        public double Average(double value)
        {
            _values[_index] = value;
            _index++;
            if (_index >= _values.Length)
            {
                _index = 0;
            }

            if (_count < _values.Length)
            {
                _count++;
            }

            var sum = 0.0;
            for (var i = 0; i < _count; i++)
            {
                sum += _values[i];
            }

            return sum / _count;
        }
    }

    private readonly record struct MmsstvDrawResult(int UpdatedPixels, int PagesDrawn, int LastLineIndex, int ProgressRows);

    private readonly record struct MmsstvDrawPageResult(int UpdatedPixels, int ProgressRows);

    private void WriteRgbRow(int lineIndex, byte[] row)
    {
        if (lineIndex >= Profile.Height)
        {
            return;
        }

        _rawRows[lineIndex] = (byte[])row.Clone();
        Buffer.BlockCopy(row, 0, _rgb, lineIndex * Profile.Width * 3, row.Length);
    }

    private void StageAuxRow(int lineIndex, short[] row)
    {
        if (lineIndex < 0 || lineIndex >= Profile.Height)
        {
            return;
        }

        _rawAuxRows[lineIndex] = (short[])row.Clone();
    }

    private string? UpdateSaveStatus()
    {
        if (_demodState.WriteLine >= Profile.Height)
        {
            Completed = true;
            ApplyMmsstvPostReceiveSlantCorrection(force: false);
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
            var r = Math.Clamp((int)((1.164457 * yv) + (1.596128 * ry[x])), 0, 255);
            var g = Math.Clamp((int)((1.164457 * yv) - (0.813022 * ry[x]) - (0.391786 * by[x])), 0, 255);
            var b = Math.Clamp((int)((1.164457 * yv) + (2.017364 * by[x])), 0, 255);
            var offset = x * 3;
            row[offset] = (byte)r;
            row[offset + 1] = (byte)g;
            row[offset + 2] = (byte)b;
        }

        return row;
    }
}
