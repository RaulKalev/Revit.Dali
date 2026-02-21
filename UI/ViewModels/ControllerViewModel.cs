using Dali.Models;
using Dali.Services.Revit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace Dali.UI.ViewModels
{
    /// <summary>
    /// ViewModel for a DALI Controller card in the Grouping tab.
    /// Contains a collection of LineViewModels and tracks aggregate load/address totals.
    /// Only one controller can be expanded at a time (accordion behaviour enforced by GroupingViewModel).
    /// </summary>
    public class ControllerViewModel : BaseViewModel
    {
        private readonly ControllerDefinition _model;
        private readonly Action<ControllerViewModel> _addLineAction;
        private readonly Action<ControllerViewModel> _deleteAction;
        private readonly Action<LineViewModel> _addToLineAction;
        private readonly Action<LineViewModel> _interactiveAddAction;
        private readonly Action<ControllerViewModel> _onExpanded; // Called so parent can collapse siblings
        private readonly Action<ControllerViewModel, string> _onNameChanged;
        private readonly Action<LineViewModel, string> _onLineNameChanged;
        private readonly Action<LineViewModel> _changeColorAction;
        private readonly Action<ControllerViewModel, DeviceDto> _onDeviceChanged;

        public ControllerViewModel(
            string panelName,
            ControllerDefinition model,
            IEnumerable<DeviceDto> availableDevices,
            Action<ControllerViewModel> addLineAction,
            Action<ControllerViewModel> deleteAction,
            Action<LineViewModel> addToLineAction,
            Action<LineViewModel> interactiveAddAction,
            Action<ControllerViewModel> onExpanded,
            Action<ControllerViewModel, string> onNameChanged = null,
            Action<LineViewModel, string> onLineNameChanged = null,
            Action<LineViewModel> changeColorAction = null,
            Action<ControllerViewModel, DeviceDto> onDeviceChanged = null)
        {
            _panelName = panelName ?? string.Empty;
            _model = model ?? throw new ArgumentNullException(nameof(model));
            AvailableDevices = new ObservableCollection<DeviceDto>(availableDevices ?? Enumerable.Empty<DeviceDto>());
            
            if (!string.IsNullOrEmpty(_model.DeviceId))
            {
                _selectedDevice = AvailableDevices.FirstOrDefault(d => d.Id == _model.DeviceId);
            }
            _addLineAction = addLineAction;
            _deleteAction = deleteAction;
            _addToLineAction = addToLineAction;
            _interactiveAddAction = interactiveAddAction;
            _onExpanded = onExpanded;
            _onNameChanged = onNameChanged;
            _onLineNameChanged = onLineNameChanged;
            _changeColorAction = changeColorAction;
            _onDeviceChanged = onDeviceChanged;

            Lines = new ObservableCollection<LineViewModel>();
            foreach (var lineDef in model.Lines)
            {
                Lines.Add(CreateLineVM(lineDef));
            }

            AddLineCommand = new RelayCommand(_ =>
            {
                if (CanAddLine)
                {
                    _addLineAction?.Invoke(this);
                    OnPropertyChanged(nameof(CanAddLine));
                }
            }, _ => CanAddLine);
            
            DeleteCommand = new RelayCommand(_ => _deleteAction?.Invoke(this));
        }

        public ControllerDefinition Model => _model;

        private string _panelName;
        public string PanelName
        {
            get => _panelName;
            set
            {
                if (SetProperty(ref _panelName, value))
                {
                    foreach (var line in Lines) line.PanelName = value;
                }
            }
        }

        public ObservableCollection<LineViewModel> Lines { get; }
        public ObservableCollection<DeviceDto> AvailableDevices { get; }

        private DeviceDto _selectedDevice;
        public DeviceDto SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                var oldDevice = _selectedDevice;
                if (SetProperty(ref _selectedDevice, value))
                {
                    _model.DeviceId = value?.Id;
                    
                    if (value != null)
                    {
                        if (value.RatedCurrentmAPerLine.HasValue)
                        {
                            MaxLoadmA = value.RatedCurrentmAPerLine.Value;
                            foreach(var line in Lines) line.MaxLoadmA = value.RatedCurrentmAPerLine.Value;
                        }
                        if (value.MaxAddressesPerLine.HasValue)
                        {
                            MaxAddressCount = value.MaxAddressesPerLine.Value;
                            foreach(var line in Lines) line.MaxAddressCount = value.MaxAddressesPerLine.Value;
                        }
                    }

                    foreach(var line in Lines) line.ControllerModelName = value?.Name;
                    
                    _onDeviceChanged?.Invoke(this, oldDevice);
                    RecalcTotals();
                    OnPropertyChanged(nameof(CanAddLine));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanAddLine => _selectedDevice == null || !_selectedDevice.DaliLines.HasValue || Lines.Count < _selectedDevice.DaliLines.Value;

        // ---- Controller Name ----

        public string Name
        {
            get => _model.Name;
            set
            {
                if (_model.Name != value)
                {
                    string oldName = _model.Name;
                    _model.Name = value;
                    OnPropertyChanged();
                    // Sync controller name to all child lines
                    foreach (var line in Lines)
                        line.ControllerName = value;
                    _onNameChanged?.Invoke(this, oldName);
                }
            }
        }

        // ---- Accordion expansion (only one controller open at a time) ----

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value)
                {
                    _onExpanded?.Invoke(this); // parent collapses siblings
                }
            }
        }

        // ---- Aggregate Gauge Data (sum of all lines in this controller) ----

        private double _totalLoadmA;
        public double TotalLoadmA
        {
            get => _totalLoadmA;
            set
            {
                if (SetProperty(ref _totalLoadmA, value))
                {
                    OnPropertyChanged(nameof(LoadRatio));
                    OnPropertyChanged(nameof(IsWarningLoad));
                    OnPropertyChanged(nameof(IsOverLoad));
                }
            }
        }

        private int _totalAddressCount;
        public int TotalAddressCount
        {
            get => _totalAddressCount;
            set
            {
                if (SetProperty(ref _totalAddressCount, value))
                {
                    OnPropertyChanged(nameof(AddressRatio));
                    OnPropertyChanged(nameof(IsWarningAddress));
                    OnPropertyChanged(nameof(IsOverAddress));
                }
            }
        }

        private double _maxLoadmA = 250.0;
        public double MaxLoadmA
        {
            get => _maxLoadmA;
            set
            {
                if (SetProperty(ref _maxLoadmA, value))
                {
                    OnPropertyChanged(nameof(LoadRatio));
                    OnPropertyChanged(nameof(IsWarningLoad));
                    OnPropertyChanged(nameof(IsOverLoad));
                }
            }
        }

        private int _maxAddressCount = 64;
        public int MaxAddressCount
        {
            get => _maxAddressCount;
            set
            {
                if (SetProperty(ref _maxAddressCount, value))
                {
                    OnPropertyChanged(nameof(AddressRatio));
                    OnPropertyChanged(nameof(IsWarningAddress));
                    OnPropertyChanged(nameof(IsOverAddress));
                }
            }
        }

        public double LoadRatio => _maxLoadmA > 0 ? _totalLoadmA / _maxLoadmA : 0.0;
        public double AddressRatio => _maxAddressCount > 0 ? (double)_totalAddressCount / _maxAddressCount : 0.0;
        
        public bool IsWarningLoad => LoadRatio >= 0.8 && LoadRatio <= 1.0;
        public bool IsOverLoad => LoadRatio > 1.0;

        public bool IsWarningAddress => AddressRatio >= 0.8 && AddressRatio <= 1.0;
        public bool IsOverAddress => AddressRatio > 1.0;

        /// <summary>Recalculates aggregate totals by summing child line gauges.</summary>
        public void RecalcTotals()
        {
            double load = 0;
            int addr = 0;
            foreach (var line in Lines) { load += line.LoadmA; addr += line.AddressCount; }
            TotalLoadmA = load;
            TotalAddressCount = addr;
        }

        // ---- Line management ----

        public LineViewModel AddNewLine()
        {
            var def = new LineDefinition { Name = $"Line {Lines.Count + 1}", ControllerName = _model.Name };
            _model.Lines.Add(def);
            var vm = CreateLineVM(def);
            vm.IsExpanded = true;
            Lines.Add(vm);
            
            // Instantly trigger a model scan for this newly created line so it picks up pre-existing devices.
            _onLineNameChanged?.Invoke(vm, def.Name);
            
            return vm;
        }

        public void DeleteLine(LineViewModel line)
        {
            if (Lines.Remove(line))
                _model.Lines.Remove(line.Model);
        }

        /// <summary>Called by GroupingViewModel after a line's Add to Line completes.</summary>
        public void OnLineAddComplete(LineViewModel line, AddToLineResult result)
        {
            line.UpdateGauges(result);
            RecalcTotals();
        }

        private LineViewModel CreateLineVM(LineDefinition def)
        {
            return new LineViewModel(
                _panelName,
                _selectedDevice?.Name,
                def,
                line => _addToLineAction?.Invoke(line),
                line => DeleteLine(line),
                line => _interactiveAddAction?.Invoke(line),
                (line, oldN) => { _onLineNameChanged?.Invoke(line, oldN); },
                line => { _changeColorAction?.Invoke(line); });
        }

        public ICommand AddLineCommand { get; }
        public ICommand DeleteCommand { get; }
    }
}
