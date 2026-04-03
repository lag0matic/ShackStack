using Microsoft.Extensions.DependencyInjection;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Infrastructure.Audio;
using ShackStack.Infrastructure.Configuration;
using ShackStack.Infrastructure.Decoders;
using ShackStack.Infrastructure.Interop;
using ShackStack.Infrastructure.Interop.BandConditions;
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
        services.AddSingleton<ICwDecoderHost, PythonCwDecoderHost>();
        services.AddSingleton<IRttyDecoderHost, PythonRttyDecoderHost>();
        services.AddSingleton<ISstvDecoderHost, PythonSstvDecoderHost>();
        services.AddSingleton<IWefaxDecoderHost, PythonWefaxDecoderHost>();
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
            var interopService = provider.GetRequiredService<IInteropService>();
            var cwDecoderHost = provider.GetRequiredService<ICwDecoderHost>();
            var rttyDecoderHost = provider.GetRequiredService<IRttyDecoderHost>();
            var sstvDecoderHost = provider.GetRequiredService<ISstvDecoderHost>();
            var wefaxDecoderHost = provider.GetRequiredService<IWefaxDecoderHost>();
            return new MainWindowViewModel(
                context.Settings,
                context.SettingsFilePath,
                radioService,
                settingsStore,
                audioService,
                waterfallService,
                waterfallRenderSource,
                bandConditionsService,
                interopService,
                cwDecoderHost,
                rttyDecoderHost,
                sstvDecoderHost,
                wefaxDecoderHost);
        });

        return services;
    }
}
