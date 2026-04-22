using ShackStack.DecoderHost.Sstv.Core;
using ShackStack.DecoderHost.Sstv.Harness;
using System.Reflection;

const int sampleRate = SstvWorkingConfig.WorkingSampleRate;
const int chunkSize = 2_048;
var outputRoot = Path.Combine(
    @"C:\Users\lag0m\Documents\ShackStack.Avalonia",
    ".tmp-sstv-harness");
Directory.CreateDirectory(outputRoot);

var toneCheck = new float[sampleRate / 20];
double tonePhase = 0.0;
for (var i = 0; i < toneCheck.Length; i++)
{
    tonePhase += (2.0 * Math.PI * 1200.0) / sampleRate;
    toneCheck[i] = (float)Math.Sin(tonePhase);
}

var toneCheckFreq = SstvAudioMath.InstantaneousFrequency(toneCheck, sampleRate);
Console.WriteLine(toneCheckFreq.Length > 0
    ? $"1200 Hz tone check avg: {toneCheckFreq.Average():0.0}"
    : "1200 Hz tone check avg: none");
Console.WriteLine(
    $"CFQC probe 1200/1500/1900/2300: {ProbeCounterTone(1200.0, sampleRate):0.000} / " +
    $"{ProbeCounterTone(1500.0, sampleRate):0.000} / " +
    $"{ProbeCounterTone(1900.0, sampleRate):0.000} / " +
    $"{ProbeCounterTone(2300.0, sampleRate):0.000}");
Console.WriteLine();

var modeNames = new[]
{
    "Martin 1",
    "Martin 2",
    "Scottie 1",
    "Scottie 2",
    "Scottie DX",
    "Robot 36",
    "PD 50",
    "PD 90",
    "PD 120",
    "PD 160",
    "PD 180",
    "PD 240",
    "PD 290",
    "AVT 90",
};

foreach (var modeName in modeNames)
{
    if (!MmsstvModeCatalog.TryResolve(modeName, out var profile))
    {
        Console.Error.WriteLine($"Could not resolve {modeName} profile.");
        continue;
    }

    RunMode(profile, outputRoot, sampleRate, chunkSize);
    Console.WriteLine();
}

RunNativeTxPrepProbe(outputRoot, sampleRate);
Console.WriteLine();

RunNativeTxRoundTripProbe(outputRoot, sampleRate, chunkSize);
Console.WriteLine();

RunTxModulatorFeatureProbe(outputRoot, sampleRate);
Console.WriteLine();

RunReceiverLifecycleScenarios(outputRoot, sampleRate, chunkSize);
Console.WriteLine();

RunAvtReceiverProbe(outputRoot, sampleRate, chunkSize);
Console.WriteLine();

RunAvtStateProbe(sampleRate);

return 0;

static void RunMode(SstvModeProfile profile, string outputRoot, int sampleRate, int chunkSize)
{
    var stem = profile.Name.ToLowerInvariant().Replace(' ', '_');
    var sourceImage = TestCardFactory.Create(profile.Width, profile.Height);
    var sourceImagePath = Path.Combine(outputRoot, $"{stem}_source.bmp");
    NativeBitmapWriter.SaveRgb24(sourceImagePath, sourceImage, profile.Width, profile.Height);

    var audio = SstvHarnessGenerator.GenerateAudio(sourceImage, profile, sampleRate);
    var wavPath = Path.Combine(outputRoot, $"{stem}_loopback.wav");
    WaveFileWriter.WriteMono16(wavPath, audio, sampleRate);

    var visDetected = VisDetector.TryDetect(audio.ToList(), 0, allowLegacyPattern: false, out var nextVisFrame, out var visProfile);
    var visProbe = SstvHarnessGenerator.ProbeMmsstvVis(audio, sampleRate);

    var receiver = new NativeSstvReceiver();
    receiver.Configure("Auto Detect", "14.230 MHz USB", 0, 0);
    receiver.Start();

    var statusLog = new List<string>();
    for (var offset = 0; offset < audio.Length; offset += chunkSize)
    {
        var count = Math.Min(chunkSize, audio.Length - offset);
        var chunk = new float[count];
        Array.Copy(audio, offset, chunk, 0, count);
        var status = receiver.HandleAudio(chunk, out _);
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusLog.Add($"{offset,8}: {status}");
        }
    }

    var logPath = Path.Combine(outputRoot, $"{stem}_status.log");
    File.WriteAllLines(logPath, statusLog.Distinct());

    string? copiedDecodePath = null;
    ImageComparisonResult? comparison = null;
    if (!string.IsNullOrWhiteSpace(receiver.LatestImagePath) && File.Exists(receiver.LatestImagePath))
    {
        copiedDecodePath = Path.Combine(outputRoot, $"{stem}_decoded.bmp");
        File.Copy(receiver.LatestImagePath, copiedDecodePath, true);
        comparison = ImageComparison.Measure(sourceImage, BitmapReader.LoadRgb24(copiedDecodePath, profile.Width, profile.Height));
    }

    Console.WriteLine($"=== {profile.Name} ===");
    Console.WriteLine($"Mode: {receiver.DetectedMode}");
    Console.WriteLine($"Origin: {receiver.SessionOrigin}");
    Console.WriteLine($"Sync: {receiver.SyncStatus}");
    Console.WriteLine($"Prominence: {receiver.LastSyncProminence:0.00}");
    Console.WriteLine($"Direct VIS: {(visDetected ? visProfile?.Name : "none")} @ frame {nextVisFrame}");
    Console.WriteLine($"VIS probe: {visProbe}");
    if (comparison is not null)
    {
        Console.WriteLine($"Mean abs error: {comparison.MeanAbsoluteError:0.00}");
        Console.WriteLine($"Channel corr: R {comparison.RedCorrelation:0.000} | G {comparison.GreenCorrelation:0.000} | B {comparison.BlueCorrelation:0.000}");
    }

    var sessionField = typeof(NativeSstvReceiver).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic);
    var session = sessionField?.GetValue(receiver);
    if (session is not null)
    {
        var rawRowsField = session.GetType().GetField("_rawRows", BindingFlags.Instance | BindingFlags.NonPublic);
        if (rawRowsField?.GetValue(session) is byte[][] rawRows)
        {
            var firstRow = rawRows.FirstOrDefault(static row => row is { Length: > 0 });
            if (firstRow is not null)
            {
                Console.WriteLine($"First raw row min/max: {firstRow.Min()} / {firstRow.Max()}");
                Console.WriteLine($"First raw row nonzero count: {firstRow.Count(static b => b != 0)}");
            }
        }

        var firstLineDebugProperty = session.GetType().GetProperty("FirstLineDebug", BindingFlags.Instance | BindingFlags.Public);
        if (firstLineDebugProperty?.GetValue(session) is string firstLineDebug && !string.IsNullOrWhiteSpace(firstLineDebug))
        {
            Console.WriteLine($"First line debug: {firstLineDebug}");
        }

        var firstChannelDebugProperty = session.GetType().GetProperty("FirstChannelDebug", BindingFlags.Instance | BindingFlags.Public);
        if (firstChannelDebugProperty?.GetValue(session) is string firstChannelDebug && !string.IsNullOrWhiteSpace(firstChannelDebug))
        {
            Console.WriteLine($"First channel debug: {firstChannelDebug}");
        }
    }

    Console.WriteLine($"Source image: {sourceImagePath}");
    Console.WriteLine($"Generated WAV: {wavPath}");
    Console.WriteLine($"Status log: {logPath}");
    Console.WriteLine(copiedDecodePath is not null
        ? $"Decoded image: {copiedDecodePath}"
        : "Decoded image: none");
}

static double ProbeCounterTone(double toneHz, int sampleRate)
{
    var counter = new MmsstvFrequencyCounter(sampleRate);
    counter.SetWidth(false);
    counter.Type = 2;
    counter.Limit = 1;

    var samples = new float[sampleRate / 8];
    double phase = 0.0;
    for (var i = 0; i < samples.Length; i++)
    {
        phase += (2.0 * Math.PI * toneHz) / sampleRate;
        samples[i] = (float)Math.Sin(phase);
    }

    var outputs = new double[samples.Length];
    for (var i = 0; i < samples.Length; i++)
    {
        outputs[i] = -counter.Process(samples[i]) / 16384.0;
    }

    return outputs.Skip(samples.Length / 4).Average();
}

static void RunAvtStateProbe(int sampleRate)
{
    var state = new MmsstvDemodState(sampleRate)
    {
        NextMode = (int)SstvModeId.Avt90
    };

    state.BeginApplyNextMode(sampleRate);
    var apply = state.AdvanceApplyNextModeStep(
        has1200Sync: true,
        shouldRequestSave: false,
        sampleRate,
        consumedSamples: (int)Math.Round(30.0 * sampleRate / 1000.0));

    var events = new List<string> { $"apply={apply}" };

    var attack = state.AdvanceEarlySyncState(
        tone1080: 0.0,
        tone1200: 0.0,
        tone1300: 0.0,
        tone1900: 0.0,
        rawPllValue: 0.0,
        sampleRate,
        consumedSamples: (int)Math.Round(10.0 * sampleRate / 1000.0));
    events.Add($"attack={attack.Event}");

    var enterExtended = state.AdvanceEarlySyncState(
        tone1080: 0.0,
        tone1200: 0.0,
        tone1300: 0.0,
        tone1900: 0.0,
        rawPllValue: 0.0,
        sampleRate,
        consumedSamples: (int)Math.Round(5.0 * sampleRate / 1000.0));
    events.Add($"extended={enterExtended.Event}");

    foreach (var (bits, label) in new[]
    {
        (0x5FA0, "period"),
        (0x40BF, "restart"),
    })
    {
        state = PrimeAvtWait(sampleRate);
        events.Add($"{label}-prime={state.SyncMode}");
        var pll = new MmsstvDemodulatorBank(sampleRate, narrow: false);
        var emitter = new ContinuousToneEmitter(sampleRate);

        var leadResult = FeedAvtToneWindowSampleAccurate(
            state,
            pll,
            emitter,
            1900.0,
            sampleRate,
            9.7646,
            events,
            $"{label}-lead");

        if (leadResult.Event is not MmsstvDemodState.EarlySyncEvent.AvtRevertToWait)
        {
            for (var i = 15; i >= 0; i--)
            {
                var tone = ((bits >> i) & 0x1) == 1 ? 1600.0 : 2200.0;
                var eventResult = FeedAvtToneWindowSampleAccurate(
                    state,
                    pll,
                    emitter,
                    tone,
                    sampleRate,
                    9.7646,
                    events,
                    $"{label}-bit{15 - i}");

                if (eventResult.Event is MmsstvDemodState.EarlySyncEvent.AvtEnterPeriodWait
                    or MmsstvDemodState.EarlySyncEvent.AvtEnterForcedStart
                    or MmsstvDemodState.EarlySyncEvent.AvtRevertToWait)
                {
                    break;
                }
            }
        }

        if (label == "period")
        {
            var follow = FeedAvtToneWindow(state, pll, emitter, 1900.0, sampleRate, 9.7646 * 0.5, events, $"{label}-follow");
            events.Add($"{label}-follow={follow.Event}");
        }
    }

    events.Add("full-sequence-prime");
    state = PrimeAvtWait(sampleRate);
    var sequencePll = new MmsstvDemodulatorBank(sampleRate, narrow: false);
    var sequenceEmitter = new ContinuousToneEmitter(sampleRate);
    var sequencePackets = BuildMmsstvAvtPackets();
    for (var packetIndex = 0; packetIndex < sequencePackets.Count; packetIndex++)
    {
        var packet = sequencePackets[packetIndex];
        var leadResult = FeedAvtToneWindowSampleAccurate(
            state,
            sequencePll,
            sequenceEmitter,
            1900.0,
            sampleRate,
            9.7646,
            events,
            $"full-p{packetIndex:D2}-lead");
        if (leadResult.Event is MmsstvDemodState.EarlySyncEvent.AvtEnterPeriodWait
            or MmsstvDemodState.EarlySyncEvent.AvtEnterForcedStart
            or MmsstvDemodState.EarlySyncEvent.AvtRevertToWait)
        {
            if (leadResult.Event is not MmsstvDemodState.EarlySyncEvent.AvtRevertToWait)
            {
                break;
            }

            continue;
        }

        var packetCompleted = false;
        for (var bitIndex = 15; bitIndex >= 0; bitIndex--)
        {
            var tone = ((packet >> bitIndex) & 0x1) == 1 ? 1600.0 : 2200.0;
            var bitResult = FeedAvtToneWindowSampleAccurate(
                state,
                sequencePll,
                sequenceEmitter,
                tone,
                sampleRate,
                9.7646,
                events,
                $"full-p{packetIndex:D2}-b{15 - bitIndex:D2}");
            if (bitResult.Event is MmsstvDemodState.EarlySyncEvent.AvtEnterPeriodWait
                or MmsstvDemodState.EarlySyncEvent.AvtEnterForcedStart)
            {
                packetCompleted = true;
                break;
            }
        }

        if (packetCompleted)
        {
            break;
        }
    }

    var outputRoot = Path.Combine(
        @"C:\Users\lag0m\Documents\ShackStack.Avalonia",
        ".tmp-sstv-harness");
    var avtLogPath = Path.Combine(outputRoot, "avt_state_probe.log");
    File.WriteAllLines(avtLogPath, events);

    Console.WriteLine("=== AVT state probe ===");
    Console.WriteLine($"State: {state.SyncMode}");
    Console.WriteLine($"SyncAvt: {state.SyncAvt}");
    Console.WriteLine($"VisData: 0x{state.VisData:X4}");
    Console.WriteLine($"Probe log: {avtLogPath}");
}

static void RunAvtReceiverProbe(string outputRoot, int sampleRate, int chunkSize)
{
    if (!MmsstvModeCatalog.TryResolve("AVT 90", out var profile))
    {
        Console.WriteLine("=== AVT receiver probe ===");
        Console.WriteLine("AVT 90 profile not found.");
        return;
    }

    var audio = SstvHarnessGenerator.GenerateAvtControlAudio(profile, sampleRate);
    var wavPath = Path.Combine(outputRoot, "avt_90_control.wav");
    WaveFileWriter.WriteMono16(wavPath, audio, sampleRate);

    var receiver = new NativeSstvReceiver();
    receiver.Configure("Auto Detect", "14.230 MHz USB", 0, 0);
    receiver.Start();

    var statusLog = new List<string>();
    for (var offset = 0; offset < audio.Length; offset += chunkSize)
    {
        var count = Math.Min(chunkSize, audio.Length - offset);
        var chunk = new float[count];
        Array.Copy(audio, offset, chunk, 0, count);
        var status = receiver.HandleAudio(chunk, out _);
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusLog.Add($"{offset,8}: {status}");
        }
    }

    var logPath = Path.Combine(outputRoot, "avt_90_receiver.log");
    File.WriteAllLines(logPath, statusLog.Distinct());

    Console.WriteLine("=== AVT receiver probe ===");
    Console.WriteLine($"Mode: {receiver.DetectedMode}");
    Console.WriteLine($"Origin: {receiver.SessionOrigin}");
    Console.WriteLine($"Sync: {receiver.SyncStatus}");
    Console.WriteLine($"Prominence: {receiver.LastSyncProminence:0.00}");
    Console.WriteLine($"Generated WAV: {wavPath}");
    Console.WriteLine($"Receiver log: {logPath}");
}

static void RunReceiverLifecycleScenarios(string outputRoot, int sampleRate, int chunkSize)
{
    if (!MmsstvModeCatalog.TryResolve("Martin 1", out var profile))
    {
        Console.WriteLine("=== Receiver lifecycle scenarios ===");
        Console.WriteLine("Martin 1 profile not found.");
        return;
    }

    var sourceImage = TestCardFactory.Create(profile.Width, profile.Height);
    var fullAudio = SstvHarnessGenerator.GenerateAudio(sourceImage, profile, sampleRate);
    var cleanEndAudio = WithTrailingSilence(fullAudio, sampleRate, 6.0);
    var dropoutAudio = WithTrailingSilence(TruncateForDropout(fullAudio, sampleRate, 6.5), sampleRate, 6.0);
    var falseStartAudio = WithTrailingSilence(SstvHarnessGenerator.GenerateVisOnlyAudio(profile, sampleRate), sampleRate, 6.0);

    var cleanEnd = RunLifecycleScenario("clean_end", cleanEndAudio, profile, outputRoot, sampleRate, chunkSize);
    var dropout = RunLifecycleScenario("dropout", dropoutAudio, profile, outputRoot, sampleRate, chunkSize);
    var falseStart = RunLifecycleScenario("false_start", falseStartAudio, profile, outputRoot, sampleRate, chunkSize);

    Console.WriteLine("=== Receiver lifecycle scenarios ===");
    PrintLifecycleScenario(cleanEnd);
    PrintLifecycleScenario(dropout);
    PrintLifecycleScenario(falseStart);
}

static void RunNativeTxPrepProbe(string outputRoot, int sampleRate)
{
    var probeModes = new[]
    {
        "Martin 1",
        "Scottie 1",
        "Robot 36",
        "PD 120",
        "AVT 90",
    };

    Console.WriteLine("=== Native TX prep probe ===");
    foreach (var modeName in probeModes)
    {
        if (!MmsstvModeCatalog.TryResolve(modeName, out var profile))
        {
            Console.WriteLine($"{modeName}: missing profile");
            continue;
        }

        var tx = MmsstvTxConfiguration.Create(profile, sampleRate);
        var tonePlan = MmsstvTxSequenceBuilder.BuildVisOnly(tx);
        var modulator = new MmsstvTxModulator(sampleRate);
        var pcm = WithLeadingAndTrailingSilence(modulator.RenderQueuedPcm(tonePlan, tx), sampleRate, 0.15, 0.15);
        var wavPath = Path.Combine(outputRoot, $"tx_probe_{profile.Name.ToLowerInvariant().Replace(' ', '_')}.wav");
        WaveFileWriter.WriteMono16(wavPath, pcm, sampleRate);

        var visDetected = VisDetector.TryDetect(pcm.ToList(), 0, allowLegacyPattern: false, out _, out var visProfile);
        Console.WriteLine($"{profile.Name}: tones {tonePlan.Count}, samples {pcm.Length}, VIS {(visDetected ? visProfile?.Name : "none")}, wav {wavPath}");
    }
}

static void RunNativeTxRoundTripProbe(string outputRoot, int sampleRate, int chunkSize)
{
    var probeModes = new[]
    {
        "Martin 1",
        "Scottie 1",
        "Robot 36",
        "PD 120",
    };

    var clipBuilder = new SstvTransmitClipBuilder(sampleRate);

    Console.WriteLine("=== Native TX round-trip probe ===");
    foreach (var modeName in probeModes)
    {
        if (!MmsstvModeCatalog.TryResolve(modeName, out var profile))
        {
            Console.WriteLine($"{modeName}: missing profile");
            continue;
        }

        var stem = $"tx_roundtrip_{profile.Name.ToLowerInvariant().Replace(' ', '_')}";
        var sourceImage = TestCardFactory.Create(profile.Width, profile.Height);
        var sourceImagePath = Path.Combine(outputRoot, $"{stem}_source.bmp");
        NativeBitmapWriter.SaveRgb24(sourceImagePath, sourceImage, profile.Width, profile.Height);

        var clip = clipBuilder.Build(profile.Name, sourceImage, profile.Width, profile.Height);
        var txAudio = Pcm16ToFloatMono(clip.PcmBytes);
        var wavPath = Path.Combine(outputRoot, $"{stem}.wav");
        WaveFileWriter.WriteMono16(wavPath, txAudio, sampleRate);
        var visDetected = VisDetector.TryDetect(txAudio.ToList(), 0, allowLegacyPattern: false, out _, out var visProfile);

        var receiver = new NativeSstvReceiver();
        receiver.Configure("Auto Detect", "14.230 MHz USB", 0, 0);
        receiver.Start();

        var statusLog = new List<string>();
        for (var offset = 0; offset < txAudio.Length; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, txAudio.Length - offset);
            var chunk = new float[count];
            Array.Copy(txAudio, offset, chunk, 0, count);
            var status = receiver.HandleAudio(chunk, out _);
            if (!string.IsNullOrWhiteSpace(status))
            {
                statusLog.Add($"{offset,8}: {status}");
            }
        }

        var logPath = Path.Combine(outputRoot, $"{stem}.log");
        File.WriteAllLines(logPath, statusLog.Distinct());

        ImageComparisonResult? comparison = null;
        string? copiedDecodePath = null;
        string? decodeFormatNote = null;
        if (!string.IsNullOrWhiteSpace(receiver.LatestImagePath) && File.Exists(receiver.LatestImagePath))
        {
            copiedDecodePath = Path.Combine(outputRoot, $"{stem}_decoded.bmp");
            File.Copy(receiver.LatestImagePath, copiedDecodePath, true);
            try
            {
                comparison = ImageComparison.Measure(
                    sourceImage,
                    BitmapReader.LoadRgb24(copiedDecodePath, profile.Width, profile.Height));
            }
            catch (InvalidDataException)
            {
                decodeFormatNote = DescribeBmp(copiedDecodePath);
            }
        }

        Console.WriteLine($"{profile.Name}: direct VIS {(visDetected ? visProfile?.Name : "none")}, mode {receiver.DetectedMode}, origin {receiver.SessionOrigin}, sync {receiver.SyncStatus}");
        if (comparison is not null)
        {
            Console.WriteLine(
                $"  round-trip MAE {comparison.MeanAbsoluteError:0.00}, corr R {comparison.RedCorrelation:0.000} | G {comparison.GreenCorrelation:0.000} | B {comparison.BlueCorrelation:0.000}");
        }
        else
        {
            Console.WriteLine("  round-trip decode: none");
            if (!string.IsNullOrWhiteSpace(decodeFormatNote))
            {
                Console.WriteLine($"  decode artifact format: {decodeFormatNote}");
            }
        }

        Console.WriteLine($"  source {sourceImagePath}");
        Console.WriteLine($"  tx wav {wavPath}");
        Console.WriteLine($"  log {logPath}");
        if (copiedDecodePath is not null)
        {
            Console.WriteLine($"  decoded {copiedDecodePath}");
        }
    }
}

static void RunTxModulatorFeatureProbe(string outputRoot, int sampleRate)
{
    Console.WriteLine("=== Native TX modulator feature probe ===");

    var modulator = new MmsstvTxModulator(sampleRate);
    var tx = MmsstvTxConfiguration.Create(MmsstvModeCatalog.Profiles.First(), sampleRate);

    modulator.InitTxBuffer();
    modulator.WriteFsk(0b101101);
    var fskSamples = new float[modulator.BufferedCount];
    for (var i = 0; i < fskSamples.Length; i++)
    {
        fskSamples[i] = (float)modulator.Do();
    }

    var fskPath = Path.Combine(outputRoot, "tx_probe_fsk.wav");
    WaveFileWriter.WriteMono16(fskPath, WithLeadingAndTrailingSilence(fskSamples, sampleRate, 0.05, 0.05), sampleRate);
    Console.WriteLine($"FSK samples: {fskSamples.Length}, wav {fskPath}");

    modulator.InitTxBuffer();
    modulator.WriteCwId('C');
    modulator.WriteCwId('Q');
    modulator.WriteCwId(' ');
    modulator.WriteCwId('@');
    var cwSamples = new float[modulator.BufferedCount];
    for (var i = 0; i < cwSamples.Length; i++)
    {
        cwSamples[i] = (float)modulator.Do();
    }

    var cwPath = Path.Combine(outputRoot, "tx_probe_cwid.wav");
    WaveFileWriter.WriteMono16(cwPath, WithLeadingAndTrailingSilence(cwSamples, sampleRate, 0.05, 0.05), sampleRate);
    Console.WriteLine($"CWID samples: {cwSamples.Length}, wav {cwPath}");

    modulator.InitTxBuffer();
    modulator.Write(1900, 100, tx);
    modulator.TuneEnabled = false;
    var queuedPcm = new float[Math.Min(64, modulator.BufferedCount)];
    for (var i = 0; i < queuedPcm.Length; i++)
    {
        queuedPcm[i] = (float)modulator.Do();
    }

    var idlePcm = new float[64];
    for (var i = 0; i < idlePcm.Length; i++)
    {
        idlePcm[i] = (float)modulator.Do();
    }

    Console.WriteLine($"Queued probe first sample: {queuedPcm[0]:0.0000}");
    Console.WriteLine($"Idle probe avg abs: {idlePcm.Select(Math.Abs).Average():0.0000}");
}

static float[] Pcm16ToFloatMono(byte[] pcmBytes)
{
    if ((pcmBytes.Length % 2) != 0)
    {
        throw new InvalidOperationException("PCM16 byte buffer length must be even.");
    }

    var samples = new float[pcmBytes.Length / 2];
    for (var i = 0; i < samples.Length; i++)
    {
        var sample = (short)(pcmBytes[i * 2] | (pcmBytes[(i * 2) + 1] << 8));
        samples[i] = sample / (float)short.MaxValue;
    }

    return samples;
}

static string DescribeBmp(string path)
{
    using var stream = File.OpenRead(path);
    using var reader = new BinaryReader(stream);

    var signature = new string(reader.ReadChars(2));
    if (!string.Equals(signature, "BM", StringComparison.Ordinal))
    {
        return $"signature={signature}";
    }

    reader.ReadInt32();
    reader.ReadInt32();
    reader.ReadInt32();
    var headerSize = reader.ReadInt32();
    if (headerSize < 40)
    {
        return $"header={headerSize}";
    }

    var width = reader.ReadInt32();
    var height = reader.ReadInt32();
    reader.ReadInt16();
    var bitsPerPixel = reader.ReadInt16();
    var compression = reader.ReadInt32();
    return $"width={width}, height={height}, bpp={bitsPerPixel}, compression={compression}";
}

static LifecycleScenarioResult RunLifecycleScenario(
    string stem,
    float[] audio,
    SstvModeProfile profile,
    string outputRoot,
    int sampleRate,
    int chunkSize)
{
    var wavPath = Path.Combine(outputRoot, $"scenario_{stem}.wav");
    WaveFileWriter.WriteMono16(wavPath, audio, sampleRate);

    var receiver = new NativeSstvReceiver();
    receiver.Configure("Auto Detect", "14.230 MHz USB", 0, 0);
    receiver.Start();

    var statuses = new List<string>();
    for (var offset = 0; offset < audio.Length; offset += chunkSize)
    {
        var count = Math.Min(chunkSize, audio.Length - offset);
        var chunk = new float[count];
        Array.Copy(audio, offset, chunk, 0, count);
        var status = receiver.HandleAudio(chunk, out _);
        if (!string.IsNullOrWhiteSpace(status))
        {
            statuses.Add($"{offset,8}: {status}");
        }
    }

    var logPath = Path.Combine(outputRoot, $"scenario_{stem}.log");
    File.WriteAllLines(logPath, statuses.Distinct());

    return new LifecycleScenarioResult(
        stem,
        wavPath,
        logPath,
        receiver.DetectedMode,
        receiver.SessionOrigin,
        receiver.SyncStatus,
        statuses.Any(static s => s.Contains("Receiving", StringComparison.OrdinalIgnoreCase)),
        statuses.Any(static s => s.Contains("Transmission ended", StringComparison.OrdinalIgnoreCase)),
        statuses.Any(static s => s.Contains("Image complete", StringComparison.OrdinalIgnoreCase)),
        receiver.LatestImagePath,
        profile.Name);
}

static void PrintLifecycleScenario(LifecycleScenarioResult result)
{
    Console.WriteLine($"Scenario: {result.Name}");
    Console.WriteLine($"Mode: {result.DetectedMode}");
    Console.WriteLine($"Origin: {result.Origin}");
    Console.WriteLine($"Final sync: {result.FinalSyncStatus}");
    Console.WriteLine($"Saw receiving: {result.SawReceiving}");
    Console.WriteLine($"Saw transmission end: {result.SawTransmissionEnd}");
    Console.WriteLine($"Saw image complete: {result.SawImageComplete}");
    Console.WriteLine($"Generated WAV: {result.WavPath}");
    Console.WriteLine($"Scenario log: {result.LogPath}");
    Console.WriteLine(!string.IsNullOrWhiteSpace(result.LatestImagePath)
        ? $"Latest image: {result.LatestImagePath}"
        : "Latest image: none");
}

static float[] TruncateForDropout(float[] audio, int sampleRate, double seconds)
{
    var keep = Math.Min(audio.Length, Math.Max(1, (int)Math.Round(seconds * sampleRate)));
    var truncated = new float[keep];
    Array.Copy(audio, truncated, keep);
    return truncated;
}

static float[] WithTrailingSilence(float[] audio, int sampleRate, double seconds)
{
    var silence = Math.Max(1, (int)Math.Round(seconds * sampleRate));
    var combined = new float[audio.Length + silence];
    Array.Copy(audio, combined, audio.Length);
    return combined;
}

static float[] WithLeadingAndTrailingSilence(float[] audio, int sampleRate, double leadSeconds, double trailSeconds)
{
    var lead = Math.Max(1, (int)Math.Round(leadSeconds * sampleRate));
    var trail = Math.Max(1, (int)Math.Round(trailSeconds * sampleRate));
    var combined = new float[lead + audio.Length + trail];
    Array.Copy(audio, 0, combined, lead, audio.Length);
    return combined;
}

static MmsstvDemodState.EarlySyncResult FeedAvtToneWindow(
    MmsstvDemodState state,
    MmsstvDemodulatorBank pll,
    ContinuousToneEmitter emitter,
    double toneHz,
    int sampleRate,
    double durationMs,
    List<string> events,
    string label)
{
    var count = Math.Max(1, (int)Math.Round(durationMs * sampleRate / 1000.0));
    var chunk = emitter.Generate(toneHz, count);

    double rawPll = 0.0;
    for (var i = 0; i < chunk.Length; i++)
    {
        rawPll = pll.ProcessRaw(chunk[i], MmsstvDemodulatorType.Pll);
    }

    var result = state.AdvanceEarlySyncState(
        tone1080: 0.0,
        tone1200: 0.0,
        tone1300: 0.0,
        tone1900: 0.0,
        rawPllValue: rawPll,
        sampleRate,
        consumedSamples: count);
    events.Add($"{label}:tone={toneHz:0.0}:pll={rawPll:0.0}:event={result.Event}");
    return result;
}

static MmsstvDemodState.EarlySyncResult FeedAvtToneWindowSampleAccurate(
    MmsstvDemodState state,
    MmsstvDemodulatorBank pll,
    ContinuousToneEmitter emitter,
    double toneHz,
    int sampleRate,
    double durationMs,
    List<string> events,
    string label)
{
    var count = Math.Max(1, (int)Math.Round(durationMs * sampleRate / 1000.0));
    var lastRawPll = 0.0;
    var minRawPll = double.PositiveInfinity;
    var maxRawPll = double.NegativeInfinity;
    var sumRawPll = 0.0;
    var lastResult = new MmsstvDemodState.EarlySyncResult(MmsstvDemodState.EarlySyncEvent.None);
    var firstEventSample = -1;
    var firstEvent = MmsstvDemodState.EarlySyncEvent.None;
    for (var i = 0; i < count; i++)
    {
        var sample = emitter.Next(toneHz);
        lastRawPll = pll.ProcessRaw(sample, MmsstvDemodulatorType.Pll);
        minRawPll = Math.Min(minRawPll, lastRawPll);
        maxRawPll = Math.Max(maxRawPll, lastRawPll);
        sumRawPll += lastRawPll;
        lastResult = state.AdvanceEarlySyncState(
            tone1080: 0.0,
            tone1200: 0.0,
            tone1300: 0.0,
            tone1900: 0.0,
            rawPllValue: lastRawPll,
            sampleRate,
            consumedSamples: 1);
        if (lastResult.Event != MmsstvDemodState.EarlySyncEvent.None && firstEventSample < 0)
        {
            firstEventSample = i;
            firstEvent = lastResult.Event;
        }
    }

    var avgRawPll = sumRawPll / count;
    events.Add(
        $"{label}:tone={toneHz:0.0}:firstEventSample={firstEventSample}:firstEvent={firstEvent}:lastEvent={lastResult.Event}:lastPll={lastRawPll:0.0}:avgPll={avgRawPll:0.0}:minPll={minRawPll:0.0}:maxPll={maxRawPll:0.0}");
    return lastResult;
}

static List<int> BuildMmsstvAvtPackets()
{
    var packets = new List<int>(capacity: 32);
    var current = 0x5FA0;
    for (var i = 0; i < 32; i++)
    {
        packets.Add(current);
        current = ((current & 0xff00) - 0x0100) | ((current & 0x00ff) + 0x0001);
    }

    return packets;
}

static MmsstvDemodState PrimeAvtWait(int sampleRate)
{
    var state = new MmsstvDemodState(sampleRate)
    {
        NextMode = (int)SstvModeId.Avt90
    };

    state.BeginApplyNextMode(sampleRate);
    state.AdvanceApplyNextModeStep(
        has1200Sync: true,
        shouldRequestSave: false,
        sampleRate,
        consumedSamples: (int)Math.Round(30.0 * sampleRate / 1000.0));
    return state;
}

sealed class ContinuousToneEmitter
{
    private readonly int _sampleRate;
    private double _phase;

    public ContinuousToneEmitter(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public float Next(double toneHz)
    {
        _phase += (2.0 * Math.PI * toneHz) / _sampleRate;
        return (float)Math.Sin(_phase);
    }

    public float[] Generate(double toneHz, int count)
    {
        var buffer = new float[count];
        for (var i = 0; i < count; i++)
        {
            buffer[i] = Next(toneHz);
        }

        return buffer;
    }
}

readonly record struct LifecycleScenarioResult(
    string Name,
    string WavPath,
    string LogPath,
    string DetectedMode,
    string Origin,
    string FinalSyncStatus,
    bool SawReceiving,
    bool SawTransmissionEnd,
    bool SawImageComplete,
    string? LatestImagePath,
    string ExpectedMode);
