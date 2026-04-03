using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;
using ShackStack.Infrastructure.Interop.Flrig;

namespace ShackStack.Infrastructure.Interop;

public sealed class InteropService : IInteropService
{
    private readonly FlrigMethodDispatcher _dispatcher = new();
    private readonly SimpleSubject<InteropEvent> _events = new();
    private readonly IAppSettingsStore _settingsStore;
    private readonly IRadioService _radioService;
    private readonly IDisposable _radioSubscription;
    private readonly IDisposable _dispatcherSubscription;
    private FlrigHttpServer? _server;
    private bool _started;

    public InteropService(IAppSettingsStore settingsStore, IRadioService radioService)
    {
        _settingsStore = settingsStore;
        _radioService = radioService;
        _radioSubscription = _radioService.StateStream.Subscribe(new Observer<RadioState>(state =>
        {
            _dispatcher.UpdateRadioState(state);
        }));
        _dispatcherSubscription = _dispatcher.Events.Subscribe(new Observer<InteropEvent>(evt => _events.OnNext(evt)));
        _dispatcher.ConfigureControlHandlers(
            (hz, ct) => _radioService.SetFrequencyAsync(hz, ct),
            (mode, ct) => _radioService.SetModeAsync(mode, ct),
            (enabled, ct) => _radioService.SetPttAsync(enabled, ct));
    }

    public IObservable<InteropEvent> Events => _events;

    public async Task StartAsync(CancellationToken ct)
    {
        if (_started)
        {
            return;
        }

        var settings = await _settingsStore.LoadAsync(ct).ConfigureAwait(false);
        if (!settings.Interop.FlrigEnabled)
        {
            _events.OnNext(new InteropEvent("flrig", "disabled"));
            return;
        }

        try
        {
            _server = new FlrigHttpServer(_dispatcher, settings.Interop.FlrigHost, settings.Interop.FlrigPort);
            await _server.StartAsync(ct).ConfigureAwait(false);
            _started = true;
            _events.OnNext(new InteropEvent("flrig", $"listening {settings.Interop.FlrigHost}:{settings.Interop.FlrigPort}"));
        }
        catch (Exception ex)
        {
            _events.OnNext(new InteropEvent("flrig", $"error {ex.Message}"));
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_server is not null)
        {
            await _server.StopAsync().ConfigureAwait(false);
            await _server.DisposeAsync().ConfigureAwait(false);
            _server = null;
        }

        _started = false;
        _events.OnNext(new InteropEvent("flrig", "stopped"));
    }
}
