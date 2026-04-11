# Atsumare

## Build

```powershell
dotnet build Atsumare.sln -p:Platform=x64
```

## MSIX Packaging

This project is configured for single-project MSIX packaging, which is the preferred path for Microsoft Store distribution.

Example Store-style package build:

```powershell
dotnet publish Atsumare\Atsumare.csproj -c Release -p:Platform=x64
```

Release packages are emitted under:

```text
Atsumare\bin\x64\Release\AppPackages\
```

The current publish profiles target packaged MSIX output for `x86`, `x64`, and `ARM64`.

## E2E Tests

E2E tests live in `Atsumare.E2E.Tests` and use FlaUI against the unpackaged WinUI app.

Default `dotnet test` skips these tests unless you explicitly enable them in an interactive Windows desktop session:

```powershell
$env:ATSUMARE_RUN_E2E='1'
dotnet test Atsumare.E2E.Tests\Atsumare.E2E.Tests.csproj -p:Platform=x64
```

The app also supports an E2E mode used by the test project:

- `ATSUMARE_E2E=1`
- `ATSUMARE_SETTINGS_PATH=<temp settings path>`
- `ATSUMARE_E2E_INSTANCE_ID=<unique id>`

In E2E mode, the app avoids tray/hotkey side effects and can be launched with `--settings` to open the settings window directly.
