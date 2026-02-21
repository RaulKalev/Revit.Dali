using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Dali.Models;

namespace Dali.Services.Core
{
    public class DeviceDatabaseService
    {
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RKTools", "DALIManager");
        private static readonly string DbFilePath = Path.Combine(AppDataFolder, "devices.json");

        private readonly ILogger _logger;
        private DeviceDatabase _database;

        public DeviceDatabaseService(ILogger logger)
        {
            _logger = logger;
            LoadDatabase();
        }

        public DeviceDatabase Database => _database;

        public event EventHandler DatabaseChanged;

        private void LoadDatabase()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                if (!File.Exists(DbFilePath))
                {
                    _logger.Info($"devices.json not found at {DbFilePath}. Creating default...");
                    CreateDefaultDatabase();
                }

                string json = File.ReadAllText(DbFilePath);
                _database = JsonConvert.DeserializeObject<DeviceDatabase>(json);
                _logger.Info($"Successfully loaded devices.json with {_database?.Devices?.Count ?? 0} devices.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load devices.json: {ex.Message}");
                // Fallback to empty to prevent null refs
                if (_database == null)
                {
                    _database = new DeviceDatabase
                    {
                        Devices = new System.Collections.Generic.List<DeviceDto>(),
                        Defaults = new DeviceDefaults()
                    };
                }
            }
        }

        private void CreateDefaultDatabase()
        {
            var defaultDbJson = @"{
  ""schemaVersion"": ""1.0"",
  ""manufacturer"": ""Helvar"",
  ""defaults"": {
    ""maxAddressesPerDaliLine"": 64,
    ""daliLineLengthMetersStandard"": 300,
    ""daliLineLengthMetersWithRepeater"": 600
  },
  ""devices"": [
    {
      ""id"": ""helvar-905"",
      ""type"": ""controller"",
      ""model"": ""905"",
      ""name"": ""905 Application Controller"",
      ""daliLines"": 1,
      ""maxAddressesPerLine"": 64,
      ""ratedCurrentmAPerLine"": 250,
      ""guaranteedCurrentmAPerLine"": 250
    },
    {
      ""id"": ""helvar-910"",
      ""type"": ""controller"",
      ""model"": ""910"",
      ""name"": ""910 Application Controller"",
      ""daliLines"": 2,
      ""maxAddressesPerLine"": 64,
      ""ratedCurrentmAPerLine"": 250,
      ""guaranteedCurrentmAPerLine"": 250
    },
    {
      ""id"": ""helvar-920"",
      ""type"": ""controller"",
      ""model"": ""920"",
      ""name"": ""920 Application Controller"",
      ""daliLines"": 2,
      ""maxAddressesPerLine"": 64,
      ""ratedCurrentmAPerLine"": 250,
      ""guaranteedCurrentmAPerLine"": 250
    },
    {
      ""id"": ""helvar-945"",
      ""type"": ""controller"",
      ""model"": ""945"",
      ""name"": ""945 DALI-2 Multi-master Application Controller"",
      ""daliLines"": 2,
      ""maxAddressesPerLine"": 64,
      ""ratedCurrentmAPerLine"": 250,
      ""guaranteedCurrentmAPerLine"": 250
    },
    {
      ""id"": ""helvar-950"",
      ""type"": ""controller"",
      ""model"": ""950"",
      ""name"": ""950 DALI-2 Multi-master Application Controller"",
      ""daliLines"": 4,
      ""maxAddressesPerLine"": 64,
      ""ratedCurrentmAPerLine"": 250,
      ""guaranteedCurrentmAPerLine"": 240
    },
    {
      ""id"": ""helvar-402"",
      ""type"": ""power_supply"",
      ""model"": ""402"",
      ""name"": ""402 DALI Power Supply"",
      ""addsCurrentmA"": 250,
      ""addsAddresses"": 0,
      ""scope"": ""per_line""
    },
    {
      ""id"": ""helvar-407"",
      ""type"": ""power_supply"",
      ""model"": ""407"",
      ""name"": ""407 Compact DALI Power Supply"",
      ""addsCurrentmA"": 64,
      ""addsAddresses"": 0,
      ""scope"": ""per_line""
    },
    {
      ""id"": ""helvar-405"",
      ""type"": ""repeater"",
      ""model"": ""405"",
      ""name"": ""405 DALI Repeater"",
      ""addsCurrentmA"": 250,
      ""addsAddresses"": 0,
      ""extendsLineLengthMetersTo"": 600,
      ""scope"": ""per_line_segment""
    },
    {
      ""id"": ""helvar-406"",
      ""type"": ""repeater"",
      ""model"": ""406"",
      ""name"": ""406 DALI Repeater"",
      ""addsCurrentmA"": 250,
      ""addsAddresses"": 0,
      ""extendsLineLengthMetersTo"": 600,
      ""scope"": ""per_line_segment""
    }
  ],
  ""rules"": [
    {
      ""id"": ""dali-address-limit"",
      ""appliesTo"": ""line"",
      ""maxAddresses"": 64
    },
    {
      ""id"": ""repeater-does-not-increase-addresses"",
      ""appliesTo"": ""repeater""
    }
  ]
}";
            File.WriteAllText(DbFilePath, defaultDbJson);
        }

        /// <summary>
        /// Retrieves only devices marked as type 'controller'.
        /// </summary>
        public System.Collections.Generic.IEnumerable<DeviceDto> GetControllers()
        {
            if (_database?.Devices == null) return Enumerable.Empty<DeviceDto>();

            return _database.Devices.Where(d => string.Equals(d.Type, "controller", StringComparison.OrdinalIgnoreCase));
        }

        public void SaveDatabase()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                string json = JsonConvert.SerializeObject(_database, Formatting.Indented);
                File.WriteAllText(DbFilePath, json);
                _logger.Info($"Successfully saved devices.json");
                DatabaseChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save devices.json: {ex.Message}");
                throw;
            }
        }

        public void AddDevice(DeviceDto device)
        {
            if (_database?.Devices == null) return;
            
            // Generate basic ID if missing
            if (string.IsNullOrWhiteSpace(device.Id))
            {
                device.Id = $"{_database.Manufacturer?.ToLower() ?? "custom"}-{Guid.NewGuid().ToString().Substring(0, 4)}";
            }
            
            _database.Devices.Add(device);
            SaveDatabase();
        }

        public void UpdateDevice(string originalId, DeviceDto modifiedDevice)
        {
            if (_database?.Devices == null) return;

            var existing = _database.Devices.FirstOrDefault(d => d.Id == originalId);
            if (existing != null)
            {
                existing.Id = modifiedDevice.Id;
                existing.Type = modifiedDevice.Type;
                existing.Model = modifiedDevice.Model;
                existing.Name = modifiedDevice.Name;
                existing.DaliLines = modifiedDevice.DaliLines;
                existing.MaxAddressesPerLine = modifiedDevice.MaxAddressesPerLine;
                existing.RatedCurrentmAPerLine = modifiedDevice.RatedCurrentmAPerLine;
                existing.GuaranteedCurrentmAPerLine = modifiedDevice.GuaranteedCurrentmAPerLine;
                existing.AddsCurrentmA = modifiedDevice.AddsCurrentmA;
                existing.AddsAddresses = modifiedDevice.AddsAddresses;
                existing.ExtendsLineLengthMetersTo = modifiedDevice.ExtendsLineLengthMetersTo;
                existing.Scope = modifiedDevice.Scope;

                SaveDatabase();
            }
        }

        public void DeleteDevice(string deviceId)
        {
            if (_database?.Devices == null) return;

            var existing = _database.Devices.FirstOrDefault(d => d.Id == deviceId);
            if (existing != null)
            {
                _database.Devices.Remove(existing);
                SaveDatabase();
            }
        }
    }
}
