using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ShackStack.Desktop.Bootstrap;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.UI.ViewModels;
using ShackStack.UI.Views;

namespace ShackStack.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddShackStackDesktop();
            serviceCollection.AddShackStackUi();
            _services = serviceCollection.BuildServiceProvider();

            var startup = _services.GetRequiredService<AppStartup>();
            var context = startup.LoadContextAsync(CancellationToken.None).GetAwaiter().GetResult();
            var appContext = _services.GetRequiredService<ShackStack.Desktop.Bootstrap.AppContext>();
            appContext.Settings = context.Settings;
            appContext.SettingsFilePath = context.SettingsFilePath;

            var window = _services.GetRequiredService<MainWindow>();
            window.DataContext = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = window;
            window.Show();

            _ = Task.Run(async () =>
            {
                try
                {
                    await startup.StartServicesAsync(appContext, CancellationToken.None).ConfigureAwait(false);
                    var radioService = _services.GetRequiredService<IRadioService>();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (window.DataContext is MainWindowViewModel vm)
                        {
                            vm.RefreshFromRadioSnapshot();

                            if (!radioService.CurrentState.IsConnected && vm.CanConnect)
                            {
                                vm.ConnectCommand.Execute(null);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (window.DataContext is MainWindowViewModel vm)
                        {
                            vm.ConnectionState = "Error";
                            vm.RadioStatusSummary = $"Startup error: {ex.Message}";
                            vm.CanConnect = true;
                            vm.IsBusy = false;
                        }
                    });
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
