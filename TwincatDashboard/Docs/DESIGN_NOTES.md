# Design Notes

This file captures small, tactical notes from the refactor (not a full spec).

## Startup and DI
- Removed `StartupUri` from `App.xaml` so `MainWindow` can be constructed through DI.
- `App.xaml.cs` now owns an `IHost` and explicitly:
  - configures Serilog
  - loads config via `IAppConfigStore`
  - shows `MainWindow`
  - stops/disposes the host on exit

## Paths and Config
- `AppConfig` no longer contains static path computation.
  - Paths are now provided by `IAppPaths` (`AppDataDirectory`, `ConfigFilePath`, `TempDirectory`, etc.).
- Config persistence is handled by `JsonAppConfigStore` (`IAppConfigStore`).

## Log Data IO
- `LogDataChannel` no longer relies on static config/global paths.
  - It is constructed with a temp directory and an `ILogger`.
  - The previous duplicated path join bug (`Path.Combine(AppConfig.FolderName, AppConfig.FolderName, ...)`) is eliminated.

## Blazor Components
- Components that previously called `AppConfigService` were updated to use `IAppConfigStore` and `IAppPaths`.

## Tests
- Added xUnit tests covering:
  - config store load/save roundtrip
  - `LogDataChannel` disk flush + load behavior
  - CSV export header/row count

