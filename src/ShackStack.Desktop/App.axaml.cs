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
    private AppStartup? _startup;
    private ShackStack.Desktop.Bootstrap.AppContext? _appContext;
    private int _shutdownStarted;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddShackStackDesktop();
            serviceCollection.AddShackStackUi();
            _services = serviceCollection.BuildServiceProvider();

            _startup = _services.GetRequiredService<AppStartup>();
            var context = _startup.LoadContextAsync(CancellationToken.None).GetAwaiter().GetResult();
            _appContext = _services.GetRequiredService<ShackStack.Desktop.Bootstrap.AppContext>();
            _appContext.Settings = context.Settings;
            _appContext.SettingsFilePath = context.SettingsFilePath;

            var window = _services.GetRequiredService<MainWindow>();
            window.DataContext = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = window;
            desktop.Exit += OnDesktopExit;
            window.Show();

            _ = Task.Run(async () =>
            {
                try
                {
                    await _startup.StartServicesAsync(_appContext, CancellationToken.None).ConfigureAwait(false);
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

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        ShutdownServices();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        ShutdownServices();
    }

    private void ShutdownServices()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        try
        {
            _startup?.StopServicesAsync(
                _appContext ?? new ShackStack.Desktop.Bootstrap.AppContext(),
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort shutdown only.
        }
        finally
        {
            _services?.Dispose();
            _services = null;
            _startup = null;
            _appContext = null;
        }
    }
}
