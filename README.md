# Revit DALI Manager

A Revit add-in for managing DALI lighting control assignments.

## Features

- **Batch Setup**: Automatically populate specific type parameters (`Dali Load`, `Dali Address Count`) for batches of selected families.
- **Grouping & Assignment**:
  - Hierarchical view of DALI Controllers and Lines.
  - Assign Revit elements to specific DALI lines using the `DALI_Line_ID` instance parameter.
  - Set DALI Controller name via the `DALI_Controller` instance parameter.
- **Capacity Monitoring**:
  - Real-time gauges showing DALI mA load and Address usage per line and per controller.
  - Configurable capacity limits (e.g. 250mA, 64 addresses) with separate defaults for controllers and lines.
  - Visual warnings when limits are exceeded.
- **Selection Integration**: "Add Selection to Line" workflow reads current Revit selection and updates assignments instantly.
- **Settings**: Configurable parameter mapping and capacity limits.

## Requirements

- Revit 2020/2021/2022/2023/2024 (Supports .NET 4.8)

## Installation

1. Build the solution.
2. The add-in file (`Dali.addin`) and DLL will be output to your Revit Addins folder (if configured in post-build events) or the `bin` directory.

## Usage

1. Open the plugin from the Revit Add-Ins tab.
2. Go to **Settings** to configure your shared parameter names and capacity limits.
3. Use the **Grouping** tab to create Controllers and Lines.
4. Select lighting fixtures/devices in Revit.
5. In the plugin, expand a Controller and Line, then click **Add Selection to Line**.
