using System;
using Microsoft.Extensions.DependencyInjection;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Infrastructure.Audio;
using ShackStack.Infrastructure.Configuration;
using ShackStack.Infrastructure.Decoders;
using ShackStack.Infrastructure.Interop;
using ShackStack.Infrastructure.Interop.BandConditions;
using ShackStack.Infrastructure.Interop.Longwave;
using ShackStack.Infrastructure.Radio;
using ShackStack.Infrastructure.Waterfall;
using ShackStack.UI.ViewModels;
using ShackStack.UI.Views;

namespace ShackStack.Desktop.Bootstrap;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShackStackDesktop(this IServiceCollection services)
    {
        services.AddSingleton<WaterfallService>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<IRadioService, RadioService>();
        services.AddSingleton<IAudioService, AudioService>();
        services.AddSingleton<IWaterfallService>(provider => provider.GetRequiredService<WaterfallService>());
        services.AddSingleton<IWaterfallRenderSource>(provider => provider.GetRequiredService<WaterfallService>());
        services.AddSingleton<IInteropService, InteropService>();
        services.AddSingleton<IBandConditionsService, HamqslBandConditionsService>();
        services.AddSingleton<ILongwaveService, LongwaveService>();
        services.AddSingleton<IClockDisciplineService, SystemClockDisciplineService>();
        services.AddSingleton<ICwDecoderHost>(provider =>
        {
            var audio = provider.GetRequiredService<IAudioService>();
            var useGgmorse = string.Equals(
                Environment.GetEnvironmentVariable("SHACKSTACK_CW_GGMORSE"),
                "1",
                StringComparison.OrdinalIgnoreCase);

            return useGgmorse
                ? new GgmorseCwDecoderHost(audio)
                : new PythonCwDecoderHost(audio);
        });
        services.AddSingleton<IRttyDecoderHost, PythonRttyDecoderHost>();
        services.AddSingleton<ISstvDecoderHost>(provider =>
        {
            var audio = provider.GetRequiredService<IAudioService>();
            var forcePython = string.Equals(
                Environment.GetEnvironmentVariable("SHACKSTACK_SSTV_PYTHON"),
                "1",
                StringComparison.OrdinalIgnoreCase);
            return forcePython
                ? new PythonSstvDecoderHost(audio)
                : new NativeSstvDecoderHost(audio);
        });
        services.AddSingleton<ISstvTransmitService, NativeSstvTransmitService>();
        services.AddSingleton<IWefaxDecoderHost, PythonWefaxDecoderHost>();
        services.AddSingleton<IWsjtxModeHost, PythonWsjtxModeHost>();
        services.AddSingleton<DecoderManager>();
        services.AddSingleton<AppStartup>();
        services.AddSingleton<AppContext>();
        services.AddSingleton<MainWindow>();
        return services;
    }

    public static IServiceCollection AddShackStackUi(this IServiceCollection services)
    {
        services.AddTransient<MainWindowViewModel>(provider =>
        {
            var context = provider.GetRequiredService<AppContext>();
            var radioService = provider.GetRequiredService<IRadioService>();
            var settingsStore = provider.GetRequiredService<IAppSettingsStore>();
            var audioService = provider.GetRequiredService<IAudioService>();
            var waterfallService = provider.GetRequiredService<IWaterfallService>();
            var waterfallRenderSource = provider.GetRequiredService<IWaterfallRenderSource>();
            var bandConditionsService = provider.GetRequiredService<IBandConditionsService>();
            var longwaveService = provider.GetRequiredService<ILongwaveService>();
            var interopService = provider.GetRequiredService<IInteropService>();
            var cwDecoderHost = provider.GetRequiredService<ICwDecoderHost>();
            var rttyDecoderHost = provider.GetRequiredService<IRttyDecoderHost>();
            var sstvDecoderHost = provider.GetRequiredService<ISstvDecoderHost>();
            var sstvTransmitService = provider.GetRequiredService<ISstvTransmitService>();
            var wefaxDecoderHost = provider.GetRequiredService<IWefaxDecoderHost>();
            var wsjtxModeHost = provider.GetRequiredService<IWsjtxModeHost>();
            return new MainWindowViewModel(
                context.Settings,
                context.SettingsFilePath,
                radioService,
                settingsStore,
                audioService,
                waterfallService,
                waterfallRenderSource,
                bandConditionsService,
                longwaveService,
                interopService,
                cwDecoderHost,
                rttyDecoderHost,
                sstvDecoderHost,
                sstvTransmitService,
                wefaxDecoderHost,
                wsjtxModeHost);
        });

        return services;
    }
}
