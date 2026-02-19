using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using System;
using Dali.Commands;

namespace Dali
{
    [AppLoader]
    public class App : IExternalApplication
    {
        public static Services.Revit.RevitExternalEventService ExternalEventService { get; private set; }
        public static Services.SettingsService SettingsService { get; private set; }
        public static Services.ParameterResolver ParameterResolver { get; private set; }
        public static Services.Core.SessionLogger Logger { get; private set; }
        private RibbonPanel ribbonPanel;

        public Result OnStartup(UIControlledApplication application)
        {
            // Initialize Logger (SessionLogger retains entries in memory for diagnostics)
            Logger = new Services.Core.SessionLogger();
            ExternalEventService = new Services.Revit.RevitExternalEventService(Logger);
            SettingsService = new Services.SettingsService(Logger);
            ParameterResolver = new Services.ParameterResolver(Logger);

            // Define the custom tab name
            string tabName = "RK Tools";

            // Try to create the custom tab (avoid exception if it already exists)
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab already exists; continue without throwing an error
            }

            // Create Ribbon Panel on the custom tab
            ribbonPanel = application.CreateOrSelectPanel(tabName, "Tools");

            // Create PushButton with embedded resource
            var duplicateSheetsButton = ribbonPanel.CreatePushButton<DaliCommand>()
                .SetLargeImage("pack://application:,,,/Dali;component/Assets/Dali.tiff")
                .SetText("Dali")
                .SetToolTip("Manage sheet duplication and batch renaming.")
                .SetLongDescription("Dali allows you to duplicate sheets in bulk, rename with find/replace, prefixes/suffixes, and preview changes before applying.")
                .SetContextualHelp("https://github.com/RaulKalev/Dali");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Trigger the update check
            ribbonPanel?.Remove();
            return Result.Succeeded;
        }

    }
}

