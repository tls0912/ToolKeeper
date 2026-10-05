# Published assembly smoke verification

This harness has no references to application projects. It loads application and third-party assemblies from the requested publish directory, verifies every loaded first-party assembly's absolute path, and runs each product in its own STA process. Application startup and `Window.Show` are never invoked. Persistence fixtures use a temporary directory removed after each run.

Build after publishing finishes:

```powershell
dotnet build tests/Obfuscation.Smoke/Obfuscation.Smoke.csproj -c Release
```

Run each product against its actual framework-dependent publish directory:

```powershell
dotnet tests/Obfuscation.Smoke/bin/Release/net10.0-windows10.0.17763.0/Obfuscation.Smoke.dll --publish-directory artifacts/obfuscar-validation/ToolKeeper --product ToolKeeper
dotnet tests/Obfuscation.Smoke/bin/Release/net10.0-windows10.0.17763.0/Obfuscation.Smoke.dll --publish-directory artifacts/obfuscar-validation/MarkPad --product MarkPad
dotnet tests/Obfuscation.Smoke/bin/Release/net10.0-windows10.0.17763.0/Obfuscation.Smoke.dll --publish-directory artifacts/obfuscar-validation/ConvAnvil --product ConvAnvil
dotnet tests/Obfuscation.Smoke/bin/Release/net10.0-windows10.0.17763.0/Obfuscation.Smoke.dll --publish-directory artifacts/obfuscar-validation/TransLamp --product TransLamp
```

The same harness can verify portable/self-contained publish directories, while its own process uses the installed .NET Windows Desktop runtime:

```powershell
dotnet tests/Obfuscation.Smoke/bin/Release/net10.0-windows10.0.17763.0/Obfuscation.Smoke.dll --publish-directory artifacts/obfuscar-validation/Hanqing-portable-verified --product Hanqing
dotnet tests/Obfuscation.Smoke/bin/Release/net10.0-windows10.0.17763.0/Obfuscation.Smoke.dll --publish-directory artifacts/obfuscar-validation/TransLamp-OfflineKit --product TransLamp
```

Exit code 0 indicates success; exceptions and contract failures return 1. Checks cover all seven first-party assemblies: ToolKeeper, ToolKeeper.UI, ToolKeeper.Desktop (CabiDock), HistoLens, Hanqing (MarkPad), ConvAnvil and TransLamp. They include compiled XAML, shared WPF resources, file hashing, seven-frame ICO generation, synthetic historical research, Markdown rendering, BOM/byte conversion, translation routing, public JSON contracts, and real private positional records used for desktop recovery, cross-process document activation and pack transactions.

Translation routing is exercised without starting Python; Markdown preview controls are constructed without initializing WebView2. Network providers, desktop takeover, protocol registration and application startup are not exercised.
