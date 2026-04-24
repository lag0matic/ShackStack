namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-faithful port of MMSSTV's CSYNCINT interval tracking.
/// </summary>
internal sealed class MmsstvSyncInterval
{
    private const int MaxSyncLine = 8;
    private readonly uint[] _syncList = new uint[MaxSyncLine];
    private readonly MmsstvIntervalParameters _parameters;

    public MmsstvSyncInterval(int sampleRate)
    {
        _parameters = MmsstvIntervalParameters.Create(sampleRate);
        Reset();
    }

    public uint SyncCount { get; private set; }
    public uint SyncAverageCount { get; private set; }
    public int SyncTime { get; private set; }
    public int SyncIntervalPosition { get; private set; }
    public int SyncIntervalMax { get; private set; }
    public int SyncPhase { get; private set; }
    public bool Narrow { get; set; }

    public void Reset()
    {
        Array.Clear(_syncList);
        SyncAverageCount = 0;
        SyncCount = 0;
        SyncIntervalMax = 0;
        SyncIntervalPosition = 0;
        SyncPhase = 0;
        SyncTime = 0;
    }

    public void BeginSyncPhase()
    {
        SyncPhase = 1;
    }

    public void ClearSyncPhase()
    {
        SyncPhase = 0;
    }

    public int SyncCheckSub(int averageMatch)
    {
        if (!MmsstvModeMap.TryToModeId((MmsstvModeCode)averageMatch, out var modeId))
        {
            return 0;
        }

        var endIndex = modeId switch
        {
            SstvModeId.Sc2_60 or SstvModeId.Sc2_120 => -1,
            SstvModeId.Robot24 or SstvModeId.Robot36 or SstvModeId.Martin2 or SstvModeId.Pd50 or SstvModeId.Pd240
                => Narrow ? -1 : MaxSyncLine - 4,
            SstvModeId.Bw8 or SstvModeId.Bw12
                => Narrow ? -1 : 0,
            SstvModeId.WraseMn73 or SstvModeId.WraseMn110 or SstvModeId.WraseMn140 or SstvModeId.WraseMc110 or SstvModeId.WraseMc140 or SstvModeId.WraseMc180
                => Narrow ? MaxSyncLine - 5 : -1,
            _ => Narrow ? -1 : MaxSyncLine - 3,
        };

        if (endIndex < 0)
        {
            return 0;
        }

        var tolerance = (uint)Math.Round(3.0 * _parameters.SampleRate / 1000.0);
        var center = _parameters.GetModeSamples(modeId);
        var low = center - tolerance;
        var high = center + tolerance;

        for (var i = MaxSyncLine - 2; i >= endIndex; i--)
        {
            var interval = _syncList[i];
            var matched = false;
            if (interval > _parameters.SyncLowest)
            {
                var divisorMax = Narrow ? 2 : 3;
                for (var divisor = 1; divisor <= divisorMax; divisor++)
                {
                    var probe = interval / (uint)divisor;
                    if (probe > low && probe < high)
                    {
                        matched = true;
                    }
                }
            }

            if (!matched)
            {
                return 0;
            }
        }

        return 1;
    }

    public int SyncCheck()
    {
        var tolerance = (uint)Math.Round(3.0 * _parameters.SampleRate / 1000.0);
        var interval = _syncList[MaxSyncLine - 1];
        for (var divisor = 1; divisor <= 3; divisor++)
        {
            var probe = interval / (uint)divisor;
            if (probe > _parameters.SyncLowest && probe < _parameters.SyncHighest)
            {
                foreach (var profile in MmsstvModeCatalog.Profiles)
                {
                    var center = _parameters.GetModeSamples(profile.Id);
                    if (center != 0 && probe > center - tolerance && probe < center + tolerance)
                    {
                        var sourceCode = MmsstvModeMap.ToMmsstvCode(profile.Id);
                        if (SyncCheckSub((int)sourceCode) != 0)
                        {
                            return (int)sourceCode + 1;
                        }
                    }
                }
            }
            else
            {
                break;
            }
        }

        return 0;
    }

    public void SyncInc()
    {
        SyncTime++;
    }

    public void SyncInc(int count)
    {
        if (count <= 0)
        {
            return;
        }

        SyncTime += count;
    }

    public void SyncTrig(int level)
    {
        SyncIntervalMax = level;
        SyncIntervalPosition = SyncTime;
    }

    public void SyncMax(int level)
    {
        if (level > SyncIntervalMax)
        {
            SyncIntervalMax = level;
            SyncIntervalPosition = SyncTime;
        }
    }

    public int SyncStart()
    {
        var syncStart = 0;
        if (SyncIntervalMax != 0)
        {
            if ((SyncIntervalPosition - SyncAverageCount) > _parameters.SyncLowestLine)
            {
                SyncAverageCount = (uint)(SyncIntervalPosition - SyncAverageCount);
                Array.Copy(_syncList, 1, _syncList, 0, MaxSyncLine - 1);
                _syncList[MaxSyncLine - 1] = SyncAverageCount;
                if (SyncAverageCount > _parameters.SyncLowest)
                {
                    syncStart = SyncCheck();
                }

                SyncAverageCount = (uint)SyncIntervalPosition;
            }

            SyncIntervalMax = 0;
        }

        return syncStart;
    }
}
