using Microsoft.Extensions.DependencyInjection;

using Serilog;

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
    Log.Information("MainWindow initialized");

  }
}