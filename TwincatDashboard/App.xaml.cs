using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;

using System.Text;
using System.Windows;

using TwincatDashboard.Models;
using TwincatDashboard.Services;

namespace TwincatDashboard;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
  protected override void OnStartup(StartupEventArgs e) {
    ConfigLogger();

    var serviceCollection = new ServiceCollection();
    serviceCollection.AddLogging(builder => {
      builder.ClearProviders();
      builder.AddSerilog(dispose: true);
    });
    serviceCollection.AddWpfBlazorWebView();
    serviceCollection.AddMasaBlazor();
    serviceCollection.AddSingleton<AdsComService>();
    serviceCollection.AddSingleton<LogDataService>();
    serviceCollection.AddSingleton<LogPlotService>();

    AppConfigService.LoadConfig(AppConfig.ConfigFileFullName);

    Resources.Add("services", serviceCollection.BuildServiceProvider());

    base.OnStartup(e);

    Log.Information("Application started");
  }

  private void ConfigLogger() {
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "TwinCAT-DashBoard")
        .Filter.ByExcluding(c =>
            c.Properties.ContainsKey("Microsoft") || c.Properties.ContainsKey("System")
        )
        .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            AppConfig.AppLogFileFullName,
            encoding: Encoding.UTF8,
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        )
        .CreateLogger();

    DispatcherUnhandledException += (sender, args) => {
      Log.Fatal(args.Exception, "UI Thread Unhandled exception");
      args.Handled = true;
    };

    TaskScheduler.UnobservedTaskException += (sender, args) => {
      Log.Error(args.Exception, "Unobserved Task Exception");
      args.SetObserved();
    };

    AppDomain.CurrentDomain.UnhandledException += (sender, args) => {
      if (args.ExceptionObject is Exception ex) {
        Log.Fatal(ex, "Non-UI Thread Unhandled exception");
      } else {
        Log.Fatal("Non-UI Thread Unhandled exception: {ExceptionObject}", args.ExceptionObject);
      }
    };
  }

  protected override void OnExit(ExitEventArgs e) {
    Log.CloseAndFlush();
    base.OnExit(e);
  }
}