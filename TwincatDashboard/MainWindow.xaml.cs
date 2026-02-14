using Microsoft.Extensions.DependencyInjection;

using Serilog;

using System.Text;
using System.Windows;

using TwincatDashboard.Models;
using TwincatDashboard.Services;

namespace TwincatDashboard;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
  public MainWindow() {
    InitializeComponent();

    var serviceCollection = new ServiceCollection();
    serviceCollection.AddWpfBlazorWebView();
    serviceCollection.AddMasaBlazor();
    serviceCollection.AddSingleton<AdsComService>();
    serviceCollection.AddSingleton<LogDataService>();
    serviceCollection.AddSingleton<LogPlotService>();

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .Filter.ByExcluding(c =>
            c.Properties.ContainsKey("Microsoft") || c.Properties.ContainsKey("System")
        )
        .WriteTo.File(
            AppConfig.AppLogFileFullName,
            encoding: Encoding.UTF8,
            rollingInterval: RollingInterval.Day
        )
        .CreateLogger();

    serviceCollection.AddSerilog(Log.Logger, true);

    AppConfigService.LoadConfig(AppConfig.ConfigFileFullName);
#if DEBUG
        serviceCollection.AddBlazorWebViewDeveloperTools();
#endif

    Resources.Add("services", serviceCollection.BuildServiceProvider());
  }
}