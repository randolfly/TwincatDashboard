using Microsoft.Extensions.DependencyInjection;

using TwincatDashboard.Services;
using TwincatDashboard.Services.Configuration;

namespace TwincatDashboard.Hosting;

public static class ServiceRegistration
{
  public static IServiceCollection AddTwincatDashboard(this IServiceCollection services)
  {
    services.AddSingleton<IAppPaths, AppPaths>();
    services.AddSingleton<IAppConfigStore, JsonAppConfigStore>();

    services.AddSingleton<AdsComService>();
    services.AddSingleton<LogDataService>();
    services.AddSingleton<LogPlotService>();

    return services;
  }
}

