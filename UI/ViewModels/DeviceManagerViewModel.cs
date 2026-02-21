using Dali.Models;
using Dali.Services;
using Dali.Services.Core;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace Dali.UI.ViewModels
{
    public class DeviceManagerViewModel : BaseViewModel
    {
        private readonly DeviceDatabaseService _databaseService;

        public DeviceManagerViewModel(DeviceDatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            
            Devices = new ObservableCollection<DeviceDto>();
            LoadDevices();

            AddNewDeviceCommand = new RelayCommand(_ => AddNewDevice());
            DeleteDeviceCommand = new RelayCommand(param => DeleteDevice(param as DeviceDto), param => param is DeviceDto);
            SaveDeviceCommand = new RelayCommand(_ => SaveDevice(), _ => SelectedDevice != null && HasChanges);
        }

        public ObservableCollection<DeviceDto> Devices { get; }

        private DeviceDto _selectedDevice;
        public DeviceDto SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    if (_selectedDevice != null)
                    {
                        // Clone the model so edits can be cancelled or verified before saving
                        ActiveEditDevice = new DeviceDto
                        {
                            Id = _selectedDevice.Id,
                            Type = _selectedDevice.Type,
                            Model = _selectedDevice.Model,
                            Name = _selectedDevice.Name,
                            DaliLines = _selectedDevice.DaliLines,
                            MaxAddressesPerLine = _selectedDevice.MaxAddressesPerLine,
                            RatedCurrentmAPerLine = _selectedDevice.RatedCurrentmAPerLine,
                            GuaranteedCurrentmAPerLine = _selectedDevice.GuaranteedCurrentmAPerLine,
                            AddsCurrentmA = _selectedDevice.AddsCurrentmA,
                            AddsAddresses = _selectedDevice.AddsAddresses,
                            ExtendsLineLengthMetersTo = _selectedDevice.ExtendsLineLengthMetersTo,
                            Scope = _selectedDevice.Scope
                        };
                    }
                    else
                    {
                        ActiveEditDevice = null;
                    }
                    HasChanges = false;
                }
            }
        }

        private DeviceDto _activeEditDevice;
        public DeviceDto ActiveEditDevice
        {
            get => _activeEditDevice;
            set
            {
                if (SetProperty(ref _activeEditDevice, value))
                {
                    OnPropertyChanged(nameof(IsController));
                }
            }
        }

        private bool _hasChanges;
        public bool HasChanges
        {
            get => _hasChanges;
            set
            {
                if (SetProperty(ref _hasChanges, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsController => ActiveEditDevice != null && string.Equals(ActiveEditDevice.Type, "controller", StringComparison.OrdinalIgnoreCase);

        public ICommand AddNewDeviceCommand { get; }
        public ICommand DeleteDeviceCommand { get; }
        public ICommand SaveDeviceCommand { get; }

        private void LoadDevices()
        {
            Devices.Clear();
            if (_databaseService.Database?.Devices != null)
            {
                foreach (var device in _databaseService.Database.Devices.OrderBy(d => d.Type).ThenBy(d => d.Model))
                {
                    Devices.Add(device);
                }
            }
            if (Devices.Count > 0)
            {
                SelectedDevice = Devices[0];
            }
        }

        private void AddNewDevice()
        {
            var newDevice = new DeviceDto
            {
                Type = "controller",
                Name = "New Device",
                Model = "Custom",
                DaliLines = 1,
                MaxAddressesPerLine = 64,
                RatedCurrentmAPerLine = 250
            };
            
            _databaseService.AddDevice(newDevice);
            LoadDevices();

            // Select the newly added record
            var added = Devices.FirstOrDefault(d => d.Id == newDevice.Id);
            if (added != null) SelectedDevice = added;
        }

        private void SaveDevice()
        {
            if (ActiveEditDevice == null || string.IsNullOrWhiteSpace(ActiveEditDevice.Id)) return;

            _databaseService.UpdateDevice(SelectedDevice.Id, ActiveEditDevice);
            HasChanges = false;
            
            // Reload grid and retain selection
            string currentId = ActiveEditDevice.Id;
            LoadDevices();
            var reloaded = Devices.FirstOrDefault(d => d.Id == currentId);
            if (reloaded != null) SelectedDevice = reloaded;
        }

        private void DeleteDevice(DeviceDto deviceToDelete)
        {
            if (deviceToDelete == null) return;
            
            _databaseService.DeleteDevice(deviceToDelete.Id);
            LoadDevices();
        }

        // Notify UI that a sub-property of the clone changed so we can enable the Save button
        public void NotifyEditChanged()
        {
            HasChanges = true;
            OnPropertyChanged(nameof(IsController));
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
