using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShackStack.Desktop.Bootstrap;

public sealed class AppStartup(
    IAppSettingsStore settingsStore,
    IRadioService radioService,
    IWaterfallService waterfallService,
    IBandConditionsService bandConditionsService,
    IInteropService interopService)
{
    private readonly IDisposable _scopeSubscription = radioService.ScopeRowStream.Subscribe(
        new Observer<WaterfallRow>(row => waterfallService.PushScopeRow(row)));

    public async Task<AppContext> LoadContextAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        return new AppContext
        {
            Settings = settings,
            SettingsFilePath = settingsStore.SettingsFilePath,
        };
    }

    public async Task StartServicesAsync(AppContext context, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        if (settings.Ui.BandConditionsEnabled)
        {
            try
            {
                await bandConditionsService.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Keep startup moving even if the background conditions feed is unavailable.
            }
        }

        if (string.Equals(settings.Radio.ControlBackend, "direct", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Radio.CivPort, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.Radio.CivPort))
        {
            try
            {
                await radioService.ConnectAsync(
                    new RadioConnectionOptions(
                        settings.Radio.CivPort,
                        settings.Radio.CivBaud,
                        settings.Radio.CivAddress),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The shell should still come up and allow manual connect if startup connect fails.
            }
        }

        try
        {
            await interopService.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Fake FLRig should not prevent the main app from starting or the radio from connecting.
        }
    }
}
