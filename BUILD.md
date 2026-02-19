# DALI Plugin - Build and Packaging Notes

## Prerequisites

- Visual Studio 2022 (17.8+) or .NET SDK 8.0+
- NuGet package restore (all Revit API references are via NuGet)
- No local Revit DLL paths required

## Build Commands

### Both targets (default)
```
dotnet build
```

### Revit 2024 only (net48)
```
dotnet build -f net48
```

### Revit 2026 only (net8.0-windows)
```
dotnet build -f net8.0-windows
```

### Release build (with code signing)
```
dotnet build -c Release
```
The Release build triggers `signtool.exe` via the `SignBuild` MSBuild target.

## Output Locations

Build output is configured to:
```
C:\Users\mibil\OneDrive\Desktop\DevDlls\Dali\
    bin\Debug\net48\           -> Revit 2024
    bin\Debug\net8.0-windows\  -> Revit 2026
    bin\Release\net48\         -> Revit 2024 (signed)
    bin\Release\net8.0-windows\-> Revit 2026 (signed)
```

The primary output DLL is `Dali.dll`. Dependencies are embedded via Costura.Fody (single-DLL distribution).

## Revit Installation

### .addin File

Place a `.addin` file in the appropriate Revit addins folder:

| Revit Version | Addins Path |
|---------------|-------------|
| 2024 | `%APPDATA%\Autodesk\Revit\Addins\2024\` |
| 2026 | `%APPDATA%\Autodesk\Revit\Addins\2026\` |

Example `Dali.addin` content:
```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Dali</Name>
    <Assembly>C:\Path\To\Dali.dll</Assembly>
    <FullClassName>Dali.App</FullClassName>
    <AddInId>YOUR-GUID-HERE</AddInId>
    <VendorId>RKTools</VendorId>
    <VendorDescription>RK Tools</VendorDescription>
  </AddIn>
</RevitAddIns>
```

> Note: If using `ricaun.Revit.UI` with `[AppLoader]`, the `.addin` file may be auto-generated depending on your build pipeline.

## Settings File

Settings are saved to:
```
%APPDATA%\Dali\settings.json
```

### Reset Settings
Delete the settings file to restore defaults:
```
del "%APPDATA%\Dali\settings.json"
```

## Assembly Details

| Property | Value |
|----------|-------|
| Assembly Name | `Dali` |
| Root Namespace | `Dali` |
| Platform | x64 |
| Dependency Embedding | Costura.Fody 6.0.0 |
| UI Framework | WPF + MaterialDesignThemes 5.2.1 |

## Known Limitations

1. **Session-scoped highlight registry**: The `HighlightRegistry` tracking is in-memory only. If Revit is closed, the registry is lost. The "Reset Overrides" button will not find filters applied in a previous session.

2. **Type parameter caching**: The `SelectionTotalsService` type cache is per-request (cleared on each `ComputeSelectionTotals` call). This is intentional to avoid stale data but means repeated calls re-read type parameters.

3. **Filter name suffix**: If a filter named `DALI_Line_<LineName>` already exists with incompatible configuration, the tool creates `DALI_Line_<LineName>__2` (up to `__10`). These suffixed filters are tracked normally by the registry.

4. **View template restrictions**: Views controlled by view templates that lock filter overrides will produce a warning but no crash.

5. **Linked model elements**: Elements from linked Revit models are ignored during selection and counted as skipped.

6. **SessionLogger capacity**: The in-memory log retains the most recent 500 entries. Older entries are evicted automatically.
