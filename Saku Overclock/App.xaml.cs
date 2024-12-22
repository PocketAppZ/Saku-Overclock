﻿using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Saku_Overclock.Activation;
using Saku_Overclock.Contracts.Services;
using Saku_Overclock.Core.Contracts.Services;
using Saku_Overclock.Core.Services;
using Saku_Overclock.Helpers;
using Saku_Overclock.Models;
using Saku_Overclock.Notifications;
using Saku_Overclock.Services;
using Saku_Overclock.SMUEngine;
using Saku_Overclock.ViewModels;
using Saku_Overclock.Views;
using WinRT.Interop;
using UIElement = Microsoft.UI.Xaml.UIElement;
using UnhandledExceptionEventArgs = Microsoft.UI.Xaml.UnhandledExceptionEventArgs;

namespace Saku_Overclock;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    private IHost? Host
    {
        get;
    }

    public static T GetService<T>()
        where T : class
    {
        if ((Current as App)!.Host!.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static IntPtr Hwnd => WindowNative.GetWindowHandle(MainWindow);

    public static WindowEx MainWindow
    {
        get;
    } = new MainWindow();

    public static UIElement? AppTitlebar
    {
        get;
        set;
    }

    private const int
        DwmwaWindowStateNormal =
            5; // Команда для отображения окна, даже если оно скрыто в трей или не видно пользователю

    public App()
    {
        InitializeComponent();

        // Текущее окно
        var currentProcess = Process.GetCurrentProcess();
        // Поиск открытого ещё одного окна приложения
        var anotherProcess = Process.GetProcesses()
            .FirstOrDefault(p => p.ProcessName == currentProcess.ProcessName && p.Id != currentProcess.Id);
        if (anotherProcess != null) // Если открыто ещё одно окно приложения
        {
            var hWnd = ActivationInvokeHandler.FindMainWindowHWND(null, "Saku Overclock");
            ActivationInvokeHandler.BringToFrontWindow(anotherProcess.MainWindowHandle);
            ActivationInvokeHandler.ChangeAllWindowState(hWnd, DwmwaWindowStateNormal);
            ActivationInvokeHandler.ChangeWindowState(hWnd, DwmwaWindowStateNormal);
            ActivationInvokeHandler.SwitchToMainWindow(hWnd, true);
            Current.Exit();
            return; // Выйти
        }

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder().UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((context, services) =>
            {
                // Default Activation Handler
                services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();
                // Other Activation Handlers
                services.AddTransient<IActivationHandler, AppNotificationActivationHandler>();
                // Core Services
                services.AddSingleton<IFileService, FileService>();
                // Services
                services.AddSingleton<IAppNotificationService, AppNotificationService>();
                services.AddSingleton<ILocalSettingsService, LocalThemeSettingsService>();
                services.AddSingleton<IAppSettingsService, AppSettingsService>();
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                services.AddTransient<INavigationViewService, NavigationViewService>();
                services.AddSingleton<IActivationService, ActivationService>();
                services.AddSingleton<IPageService, PageService>();
                services.AddSingleton<INavigationService, NavigationService>();
                // Views and ViewModels
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<КулерViewModel>();
                services.AddTransient<КулерPage>();
                services.AddTransient<AdvancedКулерViewModel>();
                services.AddTransient<AdvancedКулерPage>();
                services.AddTransient<AsusКулерViewModel>();
                services.AddTransient<AsusКулерPage>();
                services.AddTransient<ИнформацияViewModel>();
                services.AddTransient<ИнформацияPage>();
                services.AddTransient<ПараметрыViewModel>();
                services.AddTransient<ПараметрыPage>();
                services.AddTransient<ПресетыViewModel>();
                services.AddTransient<ПресетыPage>();
                services.AddTransient<ГлавнаяViewModel>();
                services.AddTransient<ГлавнаяPage>();
                services.AddTransient<ShellPage>();
                services.AddTransient<ShellViewModel>();
                services.AddTransient<ОбновлениеPage>();
                services.AddTransient<ОбновлениеViewModel>();
                services.AddTransient<MainWindow.Applyer>();
                services.AddTransient<SendSmuCommand>();
                // Configuration
                services.Configure<LocalSettingsOptions>(
                    context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            }).Build();
        GetService<IAppNotificationService>().Initialize();
        UnhandledException += App_UnhandledException;
    }

    #region JSON and Initialization

    private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logFilePath = Environment.GetFolderPath(Environment.SpecialFolder.Personal) +
                          @"\SakuOverclock\ERRORlog.txt";
        var sw = new StreamWriter(logFilePath, true);
        sw.WriteLine(e.ToString());
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            base.OnLaunched(args);
            GetService<IAppNotificationService>().Show(string.Format("AppNotificationSamplePayload".GetLocalized(),
                AppContext.BaseDirectory));

           await GetService<IActivationService>().ActivateAsync(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            Current.Exit();
        }
    }

    #endregion
}