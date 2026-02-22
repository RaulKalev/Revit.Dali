using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Dali.Models
{
    public class SettingsModel
    {
        // 0. Settings Version for Migration
        public int Version { get; set; } = 4;

        // 1. Included Categories
        public List<BuiltInCategory> IncludedCategories { get; set; } = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_ElectricalFixtures
        };

        // 2. Parameter Mappings
        public string Param_Load { get; set; } = "Dali mA";
        public string Param_AddressCount { get; set; } = "Dali @";
        public string Param_LineId { get; set; } = "Dali siin";
        public string Param_Controller { get; set; } = "Dali kontroller";

        // 6. Visualization
        /// <summary>Instance parameter used to group devices in the controller visualization panel.</summary>
        public string DeviceGroupingParameterName { get; set; } = string.Empty;

        // 3. DALI Limits (Deprecated in Phase 6 - Controlled by devices.json)

        // 4. Persistence
        // DEPRECATED: Old flat list (kept for migration)
        public List<LineDefinition> SavedLines { get; set; } = new List<LineDefinition>();

        // NEW: Hierarchical structure
        public List<ControllerDefinition> SavedControllers { get; set; } = new List<ControllerDefinition>();

        // NEW: Top-level Panels
        public List<PanelDefinition> SavedPanels { get; set; } = new List<PanelDefinition>();

        // 5. UI State
        public bool IsDarkMode { get; set; } = true;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = double.NaN;
        public double WindowHeight { get; set; } = double.NaN;
    }
}
