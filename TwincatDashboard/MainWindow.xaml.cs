using System.Text;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TwincatDashboard.Models;
using TwincatDashboard.Services;
using TwincatDashboard.Services.IService;

namespace TwincatDashboard;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddWpfBlazorWebView();
        serviceCollection.AddMasaBlazor();
        serviceCollection.AddSingleton<IAdsComService, AdsComService>();
        serviceCollection.AddSingleton<ILogDataService, LogDataService>();
        serviceCollection.AddSingleton<ILogPlotService, LogPlotService>();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Filter.ByExcluding(c =>
                c.Properties.ContainsKey("Microsoft") || c.Properties.ContainsKey("System")
            )
            .WriteTo.Debug(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                AppConfig.AppLogFileFullName,
                encoding: Encoding.UTF8,
                rollingInterval: RollingInterval.Day
            )
            .CreateLogger();

        serviceCollection.AddSerilog(logger: Log.Logger, dispose: true);

        AppConfigService.LoadConfig(AppConfig.ConfigFileFullName);
#if DEBUG
        serviceCollection.AddBlazorWebViewDeveloperTools();
#endif

        Resources.Add("services", serviceCollection.BuildServiceProvider());
    }
}
