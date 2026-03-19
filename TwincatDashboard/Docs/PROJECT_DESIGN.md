# TwinCAT Dashboard (WPF Blazor Hybrid) Design Doc

## Context
This repository is a WPF application hosting a Blazor UI via `BlazorWebView` (Blazor Hybrid). The app connects to a TwinCAT PLC via ADS, allows selecting symbols, logging data (quick/slow), exporting data, and plotting in WPF windows.

## Goals
- Use modern .NET patterns for app composition: dependency injection, logging, configuration, and deterministic lifetime management.
- Keep the UI and service layer testable by minimizing static/global state.
- Make file-system paths (config, logs, temp files) explicit and injectable.
- Add unit tests for the services where feasible (config persistence, log data export/IO).

## Non-Goals
- Redesign UI/UX or switch UI framework.
- Rewrite TwinCAT ADS integration into a fully mocked abstraction (this would be a larger effort).
- Replace plotting implementation (WPF windows) with a different renderer.

## High-Level Architecture
- **Host / Composition Root (WPF `App`)**
  - Builds an `IHost` to own DI container + logging.
  - Loads persisted user config on startup.
  - Creates `MainWindow` from DI and assigns the service provider to `BlazorWebView`.
  - Stops the host and flushes logs on shutdown.

- **UI Layer (Blazor Components)**
  - Blazor components use DI (`@inject`) to access services:
    - `AdsComService` (PLC access)
    - `LogDataService` (data buffering/export)
    - `LogPlotService` (plot window orchestration)
    - `IAppConfigStore` (persistent app config)
    - `IAppPaths` (app-local directories)

- **Service Layer**
  - `JsonAppConfigStore` persists `AppConfig` as JSON.
  - `LogDataService` manages channels for quick logs, slow logs, and exports.
  - `LogDataChannel` is responsible for buffering and writing temporary channel data to disk.

## Configuration and Persistence
- `IAppPaths` defines where the application stores:
  - App data directory (LocalAppData)
  - Config file (`*.json`)
  - Log directory and rolling log file
  - Temp directory for quick-log channels
- `IAppConfigStore` owns the current `AppConfig` instance in memory and persists it to disk.
  - The UI mutates `ConfigStore.Current.*` and calls `SaveAsync()` to persist.

## Logging
- Serilog is configured at startup:
  - Debug sink (for development)
  - Rolling file sink under the app data directory
- Unhandled exceptions (UI thread, background tasks) are captured and logged.

## Testing Strategy
- Prefer unit tests for:
  - JSON config persistence (`JsonAppConfigStore`)
  - log channel flushing and file loading (`LogDataChannel`)
  - export formatting to CSV (`LogDataService.ExportDataAsync`)
- ADS integration is not unit-tested here due to external device dependency; consider adding an adapter interface later if deeper tests are needed.

## Future Enhancements (Optional)
- Introduce an `IAdsClient` adapter to allow true unit tests for `AdsComService`.
- Add cancellation tokens throughout long-running operations and pass them down from UI.
- Consider a structured state container (e.g., a scoped state service) for complex UI state.

