namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-shaped receive ring for MMSSTV's CSSTVDEM::m_Buf/m_B12 pages.
/// CSSTVDEM::Do continuously writes demodulated picture and sync values into
/// fixed-width pages, then DrawSSTV consumes whole pages. Keeping that seam
/// lets the port move mode drawing over without inventing a second RX model.
/// </summary>
internal sealed class MmsstvReceiveBuffer
{
    private enum ReceiveBandPassWidth
    {
        Wide = 1,
        Narrow = 2,
        VeryNarrow = 3
    }

    private const ReceiveBandPassWidth DefaultBandPassWidth = ReceiveBandPassWidth.Wide;
    private readonly SstvModeProfile _profile;
    private readonly int _sampleRate;
    private readonly bool _syncRestart;
    private readonly MmsstvFirFilter _bandPass = new();
    private readonly MmsstvDemodulatorBank _demodulators;
    private readonly MmsstvAfcTracker _afc = new();
    private readonly MmsstvLevelTracker _levelTracker;
    private readonly MmsstvSyncFilterBank _syncFilters;
    private readonly MmsstvSyncToneBank _syncToneBank = new();
    private readonly MmsstvAfcParameters _afcParameters;
    private readonly short[] _pictureBuffer;
    private readonly short[] _syncBuffer;
    private readonly int _bufferStride;
    private readonly int _guardSamples;
    private int _writeCount;
    private int _writePage;
    private int _writeBase;
    private int _writeLine;
    private int _readPage;
    private int _readBase;
    private int _readLine;
    private double _lastInput;
    private double _minFiltered = double.PositiveInfinity;
    private double _maxFiltered = double.NegativeInfinity;
    private double _minRaw = double.PositiveInfinity;
    private double _maxRaw = double.NegativeInfinity;
    private short _minPicture = short.MaxValue;
    private short _maxPicture = short.MinValue;
    private short _minSync = short.MaxValue;
    private short _maxSync = short.MinValue;
    private short _lastPicture = -16384;
    private int _skipSamples;

    public MmsstvReceiveBuffer(SstvModeProfile profile, int sampleRate, bool syncRestart)
    {
        _profile = profile;
        _sampleRate = sampleRate;
        _syncRestart = syncRestart;
        Width = MmsstvTimingEngine.CalculateLineSamples(profile, sampleRate);
        _guardSamples = Math.Max(1, MmsstvPictureGeometry.Create(profile, sampleRate).BlackAdjustSamples * 2);
        _bufferStride = Math.Max(Width + _guardSamples, (int)(1400.0 * sampleRate / 1000.0));
        _pictureBuffer = new short[MmsstvDemodState.DemodBufferMax * _bufferStride];
        _syncBuffer = new short[MmsstvDemodState.DemodBufferMax * _bufferStride];
        _demodulators = new MmsstvDemodulatorBank(sampleRate, profile.Narrow);
        _levelTracker = new MmsstvLevelTracker(sampleRate) { FastAgc = true };
        _syncFilters = new MmsstvSyncFilterBank(sampleRate);
        _afcParameters = MmsstvAfcParameters.Create(profile, sampleRate);
        _afc.Configure(_afcParameters);
        _syncToneBank.InitTone(0);
        ConfigureReceiveBandPass();
        InitializePictureGuards();
    }

    public int Width { get; }
    public int BufferStride => _bufferStride;
    public int TotalSamplesWritten { get; private set; }
    public int AvailablePages => (_writePage - _readPage + MmsstvDemodState.DemodBufferMax) % MmsstvDemodState.DemodBufferMax;
    public int WritePage => _writePage;
    public int ReadPage => _readPage;
    public int WriteBase => _writeBase;
    public int ReadBase => _readBase;
    public int WriteLine => _writeLine;
    public int ReadLine => _readLine;
    public int LostSamplesInserted { get; private set; }
    public int SkippedSamplesConsumed { get; private set; }
    public int PendingSkipSamples => _skipSamples;
    public DebugSnapshot Debug => new(
        FiniteOrZero(_minFiltered),
        FiniteOrZero(_maxFiltered),
        FiniteOrZero(_minRaw),
        FiniteOrZero(_maxRaw),
        _minPicture == short.MaxValue ? (short)0 : _minPicture,
        _maxPicture == short.MinValue ? (short)0 : _maxPicture,
        _minSync == short.MaxValue ? (short)0 : _minSync,
        _maxSync == short.MinValue ? (short)0 : _maxSync,
        LostSamplesInserted,
        SkippedSamplesConsumed,
        PendingSkipSamples);

    public void Append(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            WriteSample(samples[i]);
        }
    }

    public void InsertLostSamples(int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return;
        }

        for (var i = 0; i < sampleCount; i++)
        {
            WriteBufferedSample(_lastPicture, sync: 0);
        }

        LostSamplesInserted += sampleCount;
    }

    public void SetSkipSamples(int sampleCount)
    {
        if (sampleCount < 0)
        {
            _skipSamples = 0;
            InsertLostSamples(-sampleCount);
            return;
        }

        _skipSamples = sampleCount;
    }

    public bool TryReadPage(out ReceivePage page)
    {
        if (_readPage == _writePage)
        {
            page = default;
            return false;
        }

        var picture = new short[_bufferStride];
        var sync = new short[_bufferStride];
        Array.Copy(_pictureBuffer, _readBase, picture, 0, _bufferStride);
        Array.Copy(_syncBuffer, _readBase, sync, 0, _bufferStride);
        page = new ReceivePage(picture, sync, _readLine, _readPage, _readBase);

        AdvanceReadPage();

        return true;
    }

    private void WriteSample(float sample)
    {
        var filtered = ApplyMmsstvFrontEnd(sample);
        TrackRange(filtered, ref _minFiltered, ref _maxFiltered);
        _levelTracker.Process(filtered);
        _levelTracker.Fix();

        var demodInput = _levelTracker.Current;
        var agcSample = Math.Clamp(_levelTracker.ApplyAgc(filtered) * 32.0, -16384.0, 16384.0);
        _syncFilters.Retune(_syncToneBank);
        var syncEnvelope = _syncFilters.ProcessScaledEnvelope(agcSample);

        // CSSTVDEM::Do writes m_B12 from the current tone filters, then SyncFreq
        // may retune those filters for following samples.
        var demodType = MmsstvDemodulatorType.Hilbert;
        var raw = _demodulators.ProcessRaw(demodInput, demodType);
        if (!_profile.Family.Equals("avt", StringComparison.OrdinalIgnoreCase) && _levelTracker.CurrentMax > 16.0)
        {
            _afc.Update(raw);
            raw += _afc.AfcDiff;
            _syncToneBank.InitTone(_afc.ToneOffsetHzInt);
        }
        TrackRange(raw, ref _minRaw, ref _maxRaw);

        var picture = ToBufferLevel(-raw);
        var sync = ToSyncLevel(syncEnvelope);
        if (_skipSamples > 0)
        {
            _skipSamples--;
            SkippedSamplesConsumed++;
            return;
        }

        WriteBufferedSample(picture, sync);
    }

    private void WriteBufferedSample(short picture, short sync)
    {
        var pictureIndex = _writeBase + _writeCount;
        var syncIndex = _writeBase + _writeCount;
        _pictureBuffer[pictureIndex] = picture;
        _syncBuffer[syncIndex] = sync;
        _lastPicture = picture;
        TrackRange(picture, ref _minPicture, ref _maxPicture);
        TrackRange(sync, ref _minSync, ref _maxSync);

        // CSSTVDEM::IncWP: m_wCnt wraps at m_WD while m_wBase advances by m_BWidth.
        TotalSamplesWritten++;
        _writeCount++;
        if (_writeCount < Width)
        {
            return;
        }

        _writeCount = 0;
        _writePage++;
        _writeLine++;
        _writeBase += _bufferStride;
        if (_writePage >= MmsstvDemodState.DemodBufferMax)
        {
            _writePage = 0;
            _writeBase = 0;
        }

        if (_writePage == _readPage)
        {
            AdvanceReadPage();
        }
    }

    private void AdvanceReadPage()
    {
        _readPage++;
        _readLine++;
        _readBase += _bufferStride;
        if (_readPage >= MmsstvDemodState.DemodBufferMax)
        {
            _readPage = 0;
            _readBase = 0;
        }
    }

    private void InitializePictureGuards()
    {
        for (var page = 0; page < MmsstvDemodState.DemodBufferMax; page++)
        {
            var guardStart = (page * _bufferStride) + Width;
            var guardLength = Math.Min(_guardSamples, _bufferStride - Width);
            if (guardLength > 0)
            {
                Array.Fill(_pictureBuffer, (short)-16384, guardStart, guardLength);
            }
        }
    }

    private short ToSyncLevel(MmsstvSyncFilterBank.SyncFilterSnapshot snapshot)
    {
        var level = _profile.Narrow ? snapshot.Tone1900 : snapshot.Tone1200;
        return (short)Math.Clamp((int)Math.Round(level), 0, short.MaxValue);
    }

    private double ApplyMmsstvFrontEnd(float sample)
    {
        var scaled = SstvAudioMath.ToMmsstvPcmScale(sample);
        var lowPassed = (scaled + _lastInput) * 0.5;
        _lastInput = scaled;
        return _bandPass.Process(lowPassed);
    }

    private void ConfigureReceiveBandPass()
    {
        var tapCount = Math.Max(1, (int)(GetBandPassTapBase(DefaultBandPassWidth) * _sampleRate / 11025.0));
        var (lowCutHz, highCutHz, attenuation) = GetReceiveBandPass();
        _bandPass.Create(
            tapCount,
            MmsstvFirFilter.FilterType.BandPass,
            _sampleRate,
            lowCutHz,
            highCutHz,
            attenuation,
            1.0);
    }

    private (double LowCutHz, double HighCutHz, double Attenuation) GetReceiveBandPass()
    {
        if (!_profile.Narrow)
        {
            return GetWideBandPass(DefaultBandPassWidth);
        }

        var (low, high) = GetNarrowBandPassLimits(_profile.Id);
        return GetNarrowBandPass(DefaultBandPassWidth, low, high);
    }

    private (double LowCutHz, double HighCutHz, double Attenuation) GetWideBandPass(ReceiveBandPassWidth width)
    {
        var low = _syncRestart ? 1100.0 : 1200.0;
        return width switch
        {
            // CSSTVDEM::CalcBPF: HBPF/HBPFS definitions for m_bpf 1..3.
            ReceiveBandPassWidth.Wide => (low, 2600.0, 20.0),
            ReceiveBandPassWidth.Narrow => (low, 2500.0, 40.0),
            ReceiveBandPassWidth.VeryNarrow => (low, 2400.0, 50.0),
            _ => (low, 2600.0, 20.0),
        };
    }

    private static (double LowCutHz, double HighCutHz, double Attenuation) GetNarrowBandPass(
        ReceiveBandPassWidth width,
        double low,
        double high)
        => width switch
        {
            // CSSTVDEM::CalcNarrowBPF.
            ReceiveBandPassWidth.Wide => (low - 200.0, high, 20.0),
            ReceiveBandPassWidth.Narrow => (low - 100.0, high, 40.0),
            ReceiveBandPassWidth.VeryNarrow => (low, high, 50.0),
            _ => (low - 200.0, high, 20.0),
        };

    private static double GetBandPassTapBase(ReceiveBandPassWidth width)
        => width switch
        {
            ReceiveBandPassWidth.Wide => 24.0,
            ReceiveBandPassWidth.Narrow => 64.0,
            ReceiveBandPassWidth.VeryNarrow => 96.0,
            _ => 24.0,
        };

    private static (double LowCutHz, double HighCutHz) GetNarrowBandPassLimits(SstvModeId modeId)
        => modeId switch
        {
            SstvModeId.WraseMn73 or
            SstvModeId.WraseMn110 or
            SstvModeId.WraseMc110 => (1600.0, 2500.0),

            SstvModeId.WraseMn140 => (1700.0, 2400.0),

            SstvModeId.WraseMc140 => (1650.0, 2500.0),

            SstvModeId.WraseMc180 => (1700.0, 2400.0),

            _ => (1600.0, 2500.0),
        };

    private static short ToBufferLevel(double rawDemodValue)
        => (short)Math.Clamp((int)Math.Round(rawDemodValue), short.MinValue, short.MaxValue);

    private static void TrackRange(double value, ref double min, ref double max)
    {
        if (value < min)
        {
            min = value;
        }

        if (value > max)
        {
            max = value;
        }
    }

    private static void TrackRange(short value, ref short min, ref short max)
    {
        if (value < min)
        {
            min = value;
        }

        if (value > max)
        {
            max = value;
        }
    }

    private static double FiniteOrZero(double value)
        => double.IsFinite(value) ? value : 0.0;

    public readonly record struct DebugSnapshot(
        double MinFiltered,
        double MaxFiltered,
        double MinRaw,
        double MaxRaw,
        short MinPicture,
        short MaxPicture,
        short MinSync,
        short MaxSync,
        int LostSamplesInserted,
        int SkippedSamplesConsumed,
        int PendingSkipSamples);

    public readonly record struct ReceivePage(
        short[] Picture,
        short[] Sync,
        int LineIndex,
        int PageIndex,
        int BaseIndex);
}
