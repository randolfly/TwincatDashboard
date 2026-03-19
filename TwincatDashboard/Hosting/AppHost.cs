using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Events;

using TwincatDashboard.Services.Configuration;

namespace TwincatDashboard.Hosting;

public static class AppHost
{
  public static IHost Build()
  {
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
      // Ensure appsettings.json is loaded from the output folder (next to the exe).
      ContentRootPath = AppContext.BaseDirectory
    });

    builder.Services.AddTwincatDashboard();
    builder.Services.AddWpfBlazorWebView();
    builder.Services.AddMasaBlazor();

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog((sp, cfg) =>
    {
      var paths = sp.GetRequiredService<IAppPaths>();

      Directory.CreateDirectory(paths.LogDirectory);

      cfg.MinimumLevel.Debug()
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
          rollingInterval: RollingInterval.Day,
          outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        );
    }, dispose: true);

    return builder.Build();
  }
}

