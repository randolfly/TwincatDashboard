using Microsoft.Extensions.Logging;

using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

using TwincatDashboard.Models;

namespace TwincatDashboard.Services.Configuration;

public sealed class JsonAppConfigStore : IAppConfigStore {
  private static readonly JsonSerializerOptions SerializerOptions = new() {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  private readonly IAppPaths _paths;
  private readonly ILogger<JsonAppConfigStore> _logger;

  public JsonAppConfigStore(IAppPaths paths, ILogger<JsonAppConfigStore> logger) {
    _paths = paths;
    _logger = logger;
  }

  public AppConfig Current { get; private set; } = new();

  public async ValueTask LoadAsync(CancellationToken cancellationToken = default) {
    var path = _paths.ConfigFilePath;
    _logger.LogInformation("Loading config from {ConfigPath}", path);

    if (!File.Exists(path))
      return;

    await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    var loaded = await JsonSerializer.DeserializeAsync<AppConfig>(fs, SerializerOptions, cancellationToken);
    Current = loaded ?? new AppConfig();
  }

  public async ValueTask SaveAsync(CancellationToken cancellationToken = default) {
    var path = _paths.ConfigFilePath;
    _logger.LogInformation("Saving config to {ConfigPath}", path);

    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(dir))
      Directory.CreateDirectory(dir);

    await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    await JsonSerializer.SerializeAsync(fs, Current, SerializerOptions, cancellationToken);
  }
}