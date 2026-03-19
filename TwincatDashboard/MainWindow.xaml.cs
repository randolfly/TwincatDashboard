using Microsoft.Extensions.Logging;

using System.Windows;

namespace TwincatDashboard;

/// <summary>
///   Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
  public MainWindow(IServiceProvider services, ILogger<MainWindow> logger) {
    InitializeComponent();

    BlazorWebView.Services = services;
    logger.LogInformation("MainWindow initialized");
  }
}

