# ApiConnectTool

A local-first desktop HTTP client for Windows, built with Avalonia UI 12, .NET 10 and
CommunityToolkit.Mvvm. Build, send, organize and validate API requests without a cloud account.

## Features

- Request tabs with query parameters, headers, raw, form and file bodies, and per-request settings
- Collections with nested folders, plus a send history with response comparison
- Collection and global environments with variable precedence and secret values
- Bearer, Basic, API key and OAuth 2.0 authorization
- Declarative assertions, JSON Schema checks and JavaScript pre-request and test scripts
- Collection runner with CSV or JSON iteration data
- Import and export of Postman collections, OpenAPI documents and cURL commands
- Cookies, redirect history, streaming downloads and a session console
- Dark, light and Windows 7-style themes
- Built-in help (Help > ApiConnectTool Help)

## Requirements

- Windows
- .NET 10 SDK

## Build, test and run

```powershell
dotnet restore ApiConnectTool.slnx
dotnet build ApiConnectTool.slnx
dotnet test ApiConnectTool.Tests/ApiConnectTool.Tests.csproj
dotnet run --project ApiConnectTool/ApiConnectTool.csproj
```

## Data and secrets

The workspace is stored under `%LOCALAPPDATA%\ApiConnectTool\`. Secret values are protected with
Windows DPAPI and are never silently written as plaintext. See
"Where your data and secrets are stored" in the in-app help for details.

## Project layout

- `ApiConnectTool/` - the application (models, view models, views, services, serialization, help)
- `ApiConnectTool.Tests/` - xUnit and Avalonia headless tests
- `ApiConnectTool.WriterTestHost/` - helper process used by the single-writer tests
- `Win7AeroTheme/` - the Windows 7-style theme

## License

MIT. See [LICENSE](LICENSE).
