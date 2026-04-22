using System.Runtime.InteropServices;
using System.Text;

namespace ShackStack.Infrastructure.Decoders;

internal static class GgmorseNative
{
    private const string LibraryFileName = "shackstack_ggmorse_bridge.dll";
    private static readonly Lock Sync = new();

    private static nint libraryHandle;
    private static bool attemptedLoad;
    private static string availabilityStatus = "ggmorse bridge not loaded";

    private static CreateDelegate? create;
    private static DestroyDelegate? destroy;
    private static ConfigureDelegate? configure;
    private static ResetDelegate? reset;
    private static PushAudioDelegate? pushAudio;
    private static TakeTextDelegate? takeText;
    private static GetStatsDelegate? getStats;

    public static bool TryGetAvailability(out string status)
    {
        lock (Sync)
        {
            if (!attemptedLoad)
            {
                TryLoadLocked();
            }

            status = availabilityStatus;
            return libraryHandle != nint.Zero;
        }
    }

    public static bool TryCreateInstance(int sampleRate, out GgmorseInstance? instance, out string status)
    {
        lock (Sync)
        {
            if (!attemptedLoad)
            {
                TryLoadLocked();
            }

            if (libraryHandle == nint.Zero || create is null || destroy is null || configure is null || reset is null || pushAudio is null || takeText is null || getStats is null)
            {
                status = availabilityStatus;
                instance = null;
                return false;
            }

            var createParams = new GgmorseCreateParams
            {
                InputSampleRateHz = sampleRate > 0 ? sampleRate : 48000.0f,
                SamplesPerFrame = 128,
            };

            if (!create(ref createParams, out var handle) || handle == nint.Zero)
            {
                status = "ggmorse bridge loaded but decoder instance creation failed";
                instance = null;
                return false;
            }

            instance = new GgmorseInstance(handle, destroy, configure, reset, pushAudio, takeText, getStats);
            status = availabilityStatus;
            return true;
        }
    }

    private static void TryLoadLocked()
    {
        attemptedLoad = true;

        var candidatePaths = new List<string>();

        var envOverride = Environment.GetEnvironmentVariable("SHACKSTACK_GGMORSE_BRIDGE_PATH");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            candidatePaths.Add(Environment.ExpandEnvironmentVariables(envOverride.Trim()));
        }

        candidatePaths.Add(Path.Combine(AppContext.BaseDirectory, "DecoderWorkers", "ggmorse", LibraryFileName));

        foreach (var path in candidatePaths.Where(File.Exists))
        {
            if (NativeLibrary.TryLoad(path, out libraryHandle))
            {
                BindExportsLocked();
                availabilityStatus = $"ggmorse bridge ready ({path})";
                return;
            }
        }

        availabilityStatus = $"ggmorse bridge missing: set SHACKSTACK_GGMORSE_BRIDGE_PATH or bundle {LibraryFileName} under DecoderWorkers\\ggmorse";
    }

    private static void BindExportsLocked()
    {
        create = Marshal.GetDelegateForFunctionPointer<CreateDelegate>(NativeLibrary.GetExport(libraryHandle, "shackstack_ggmorse_create"));
        destroy = Marshal.GetDelegateForFunctionPointer<DestroyDelegate>(NativeLibrary.GetExport(libraryHandle, "shackstack_ggmorse_destroy"));
        configure = Marshal.GetDelegateForFunctionPointer<ConfigureDelegate>(NativeLibrary.GetExport(libraryHandle, "shackstack_ggmorse_configure"));
        reset = Marshal.GetDelegateForFunctionPointer<ResetDelegate>(NativeLibrary.GetExport(libraryHandle, "shackstack_ggmorse_reset"));
        pushAudio = Marshal.GetDelegateForFunctionPointer<PushAudioDelegate>(NativeLibrary.GetExport(libraryHandle, "shackstack_ggmorse_push_audio_f32"));
        takeText = Marshal.GetDelegateForFunctionPointer<TakeTextDelegate>(NativeLibrary.GetExport(libraryHandle, "shackstack_ggmorse_take_text_utf8"));
        getStats = Marshal.GetDelegateForFunctionPointer<GetStatsDelegate>(NativeLibrary.GetExport(libraryHandle, "shackstack_ggmorse_get_stats"));
    }

    internal sealed class GgmorseInstance : IDisposable
    {
        private readonly DestroyDelegate destroy;
        private readonly ConfigureDelegate configure;
        private readonly ResetDelegate reset;
        private readonly PushAudioDelegate pushAudio;
        private readonly TakeTextDelegate takeText;
        private readonly GetStatsDelegate getStats;

        private bool disposed;

        internal GgmorseInstance(
            nint handle,
            DestroyDelegate destroy,
            ConfigureDelegate configure,
            ResetDelegate reset,
            PushAudioDelegate pushAudio,
            TakeTextDelegate takeText,
            GetStatsDelegate getStats)
        {
            Handle = handle;
            this.destroy = destroy;
            this.configure = configure;
            this.reset = reset;
            this.pushAudio = pushAudio;
            this.takeText = takeText;
            this.getStats = getStats;
        }

        public nint Handle { get; }

        public bool Configure(float pitchHz, float wpm, bool autoPitch, bool autoSpeed)
        {
            var centerPitch = pitchHz > 0.0f ? pitchHz : 700.0f;
            var minPitch = Math.Clamp(centerPitch - 100.0f, 200.0f, 1200.0f);
            var maxPitch = Math.Clamp(centerPitch + 100.0f, 200.0f, 1200.0f);

            var parameters = new GgmorseDecodeParams
            {
                PitchHz = autoPitch ? -1.0f : pitchHz,
                SpeedWpm = autoSpeed ? -1.0f : wpm,
                FrequencyMinHz = minPitch,
                FrequencyMaxHz = maxPitch,
                AutoPitch = autoPitch,
                AutoSpeed = autoSpeed,
                ApplyHighPass = true,
                ApplyLowPass = true,
            };

            return configure(Handle, ref parameters);
        }

        public bool Reset() => reset(Handle);

        public bool PushAudio(ReadOnlySpan<float> samples)
        {
            if (samples.IsEmpty)
            {
                return true;
            }

            var copy = samples.ToArray();
            return pushAudio(Handle, copy, copy.Length);
        }

        public string TakeText()
        {
            var buffer = new byte[1024];
            var length = takeText(Handle, buffer, buffer.Length);
            if (length <= 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buffer, 0, length);
        }

        public bool TryGetStats(out GgmorseStats stats)
        {
            return getStats(Handle, out stats);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            destroy(Handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GgmorseCreateParams
    {
        public float InputSampleRateHz;
        public int SamplesPerFrame;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GgmorseDecodeParams
    {
        public float PitchHz;
        public float SpeedWpm;
        public float FrequencyMinHz;
        public float FrequencyMaxHz;
        [MarshalAs(UnmanagedType.I1)] public bool AutoPitch;
        [MarshalAs(UnmanagedType.I1)] public bool AutoSpeed;
        [MarshalAs(UnmanagedType.I1)] public bool ApplyHighPass;
        [MarshalAs(UnmanagedType.I1)] public bool ApplyLowPass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GgmorseStats
    {
        public float EstimatedPitchHz;
        public float EstimatedSpeedWpm;
        public float SignalThreshold;
        public float CostFunction;
        public int LastDecodeResult;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool CreateDelegate(ref GgmorseCreateParams parameters, out nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DestroyDelegate(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool ConfigureDelegate(nint handle, ref GgmorseDecodeParams parameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool ResetDelegate(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool PushAudioDelegate(nint handle, float[] samples, int sampleCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int TakeTextDelegate(nint handle, byte[] buffer, int bufferSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool GetStatsDelegate(nint handle, out GgmorseStats stats);
}
