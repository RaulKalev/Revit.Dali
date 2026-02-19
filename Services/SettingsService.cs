using System;
using System.IO;
using Newtonsoft.Json;
using Dali.Models;
using Dali.Services.Core;

namespace Dali.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private readonly ILogger _logger;

        public SettingsService(ILogger logger)
        {
            _logger = logger;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "RKTools", "DALIManager");
            Directory.CreateDirectory(folder);
            _settingsPath = Path.Combine(folder, "settings.json");
        }

        public event EventHandler<SettingsModel> OnSettingsSaved;

        public SettingsModel Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var settings = JsonConvert.DeserializeObject<SettingsModel>(json);
                    if (settings != null)
                    {
                        _logger.Info($"Loaded settings version: {settings.Version}");
                        if (settings.Version < 3)
                        {
                            settings.Version = 3;
                            settings.Param_Load = "Dali mA";
                            settings.Param_AddressCount = "Dali @";
                            settings.Param_LineId = "Dali siin";
                            settings.Param_Controller = "Dali kontroller";
                            Save(settings);
                            _logger.Info("Migrated settings to version 3.");
                        }

                        // Migrate flat SavedLines -> hierarchical SavedControllers
                        if (settings.SavedLines != null && settings.SavedLines.Count > 0
                            && (settings.SavedControllers == null || settings.SavedControllers.Count == 0))
                        {
                            _logger.Info("Migrating flat SavedLines to SavedControllers hierarchy...");
                            settings.SavedControllers = new System.Collections.Generic.List<ControllerDefinition>();

                            var grouped = new System.Collections.Generic.Dictionary<string, ControllerDefinition>();
                            foreach (var line in settings.SavedLines)
                            {
                                string ctrlKey = string.IsNullOrWhiteSpace(line.ControllerName)
                                    ? "Default Controller"
                                    : line.ControllerName.Trim();

                                if (!grouped.TryGetValue(ctrlKey, out var ctrl))
                                {
                                    ctrl = new ControllerDefinition { Name = ctrlKey, Lines = new System.Collections.Generic.List<LineDefinition>() };
                                    grouped[ctrlKey] = ctrl;
                                    settings.SavedControllers.Add(ctrl);
                                }
                                ctrl.Lines.Add(line);
                            }

                            settings.SavedLines.Clear();
                            Save(settings);
                            _logger.Info($"Migrated {settings.SavedControllers.Count} controller(s).");
                        }

                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load settings. Using defaults.", ex);
            }

            return CreateDefaultIfMissing();
        }

        public void Save(SettingsModel settings)
        {
            try
            {
                // Ensure version is always current before saving
                settings.Version = 3; 
                _logger.Info($"Saving settings version: {settings.Version}");

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
                _logger.Info("Settings saved successfully.");
                OnSettingsSaved?.Invoke(this, settings);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save settings.", ex);
            }
        }

        public SettingsModel CreateDefaultIfMissing()
        {
            var defaultSettings = new SettingsModel();
            Save(defaultSettings);
            return defaultSettings;
        }
    }
}
