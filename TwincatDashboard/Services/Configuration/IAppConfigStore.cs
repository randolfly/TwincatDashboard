using TwincatDashboard.Models;

namespace TwincatDashboard.Services.Configuration;

public interface IAppConfigStore {
  AppConfig Current { get; }

  ValueTask LoadAsync(CancellationToken cancellationToken = default);
  ValueTask SaveAsync(CancellationToken cancellationToken = default);
}