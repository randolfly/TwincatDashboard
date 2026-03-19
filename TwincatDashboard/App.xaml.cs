using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Events;

using System.IO;
using System.Text;
using System.Windows;

using TwincatDashboard.Services;
using TwincatDashboard.Services.Configuration;

namespace TwincatDashboard;

/// <summary>
///   Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
  private IHost? _host;

  protected override void OnStartup(StartupEventArgs e) {
    _host = BuildHost();

    ConfigureUnhandledExceptionLogging();

    _host.Start();

    var configStore = _host.Services.GetRequiredService<IAppConfigStore>();
    configStore.LoadAsync().GetAwaiter().GetResult();

    MainWindow = _host.Services.GetRequiredService<MainWindow>();
    MainWindow.Show();

    base.OnStartup(e);
  }

  private static IHost BuildHost() {
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings {
      // Ensure appsettings.json is loaded from the output folder (next to the exe).
      ContentRootPath = AppContext.BaseDirectory
    });

    var paths = new AppPaths();
    builder.Services.AddSingleton<IAppPaths>(paths);
    builder.Services.AddSingleton<IAppConfigStore, JsonAppConfigStore>();

    builder.Services.AddWpfBlazorWebView();
    builder.Services.AddMasaBlazor();

    builder.Services.AddSingleton<AdsComService>();
    builder.Services.AddSingleton<LogDataService>();
    builder.Services.AddSingleton<LogPlotService>();
    builder.Services.AddSingleton<MainWindow>();

    Directory.CreateDirectory(paths.LogDirectory);
    Log.Logger = new LoggerConfiguration()
      .MinimumLevel.Debug()
      .MinimumLevel.Override("Masa", LogEventLevel.Warning)
      .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
      .MinimumLevel.Override("System", LogEventLevel.Information)
      .MinimumLevel.Override("TwincatDashboard", LogEventLevel.Debug)
      .Enrich.FromLogContext()
      .Enrich.WithMachineName()
      .Enrich.WithThreadId()
      .Enrich.WithProperty("Application", "TwinCAT-DashBoard")
      .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
      .WriteTo.File(
        paths.LogFilePath,
        encoding: Encoding.UTF8,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
      )
      .CreateLogger();

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog(Log.Logger, dispose: true);

    Log.Information("Host built and logging configured");
    return builder.Build();
  }

  private void ConfigureUnhandledExceptionLogging() {
    DispatcherUnhandledException += (_, args) => {
      Log.Fatal(args.Exception, "UI thread unhandled exception");
      args.Handled = true;
    };

    TaskScheduler.UnobservedTaskException += (_, args) => {
      Log.Error(args.Exception, "Unobserved task exception");
      args.SetObserved();
    };

    AppDomain.CurrentDomain.UnhandledException += (_, args) => {
      if (args.ExceptionObject is Exception ex)
        Log.Fatal(ex, "Non-UI thread unhandled exception");
      else
        Log.Fatal("Non-UI thread unhandled exception: {ExceptionObject}", args.ExceptionObject);
    };
  }

  protected override void OnExit(ExitEventArgs e) {
    try {
      _host?.StopAsync().GetAwaiter().GetResult();
      _host?.Dispose();
    } finally {
      Log.CloseAndFlush();
      base.OnExit(e);
    }
  }
}
