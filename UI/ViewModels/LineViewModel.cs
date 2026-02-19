using Dali.Models;
using Dali.Services.Revit;
using System;
using System.Windows.Input;

namespace Dali.UI.ViewModels
{
    public class LineViewModel : BaseViewModel
    {
        private readonly LineDefinition _model;
        private readonly Action<LineViewModel> _addToLineAction;
        private readonly Action<LineViewModel> _deleteAction;

        public LineViewModel(LineDefinition model, Action<LineViewModel> addToLineAction, Action<LineViewModel> deleteAction)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _addToLineAction = addToLineAction;
            _deleteAction = deleteAction;

            AddToLineCommand = new RelayCommand(_ => _addToLineAction?.Invoke(this));
            DeleteCommand = new RelayCommand(_ => _deleteAction?.Invoke(this));
        }

        public LineDefinition Model => _model;

        public string Name
        {
            get => _model.Name;
            set
            {
                if (_model.Name != value)
                {
                    _model.Name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ControllerName
        {
            get => _model.ControllerName;
            set
            {
                if (_model.ControllerName != value)
                {
                    _model.ControllerName = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        // ---- Per-Line Gauge Data (updated after each "Add to Line" operation) ----

        private double _loadmA;
        public double LoadmA
        {
            get => _loadmA;
            set
            {
                if (SetProperty(ref _loadmA, value))
                {
                    OnPropertyChanged(nameof(LoadRatio));
                    OnPropertyChanged(nameof(IsOverLoad));
                }
            }
        }

        private int _addressCount;
        public int AddressCount
        {
            get => _addressCount;
            set
            {
                if (SetProperty(ref _addressCount, value))
                {
                    OnPropertyChanged(nameof(AddressRatio));
                    OnPropertyChanged(nameof(IsOverAddress));
                }
            }
        }

        // Limits mirror the controller defaults; controller sets these before each refresh
        private double _maxLoadmA = 250.0;
        public double MaxLoadmA
        {
            get => _maxLoadmA;
            set
            {
                if (SetProperty(ref _maxLoadmA, value))
                {
                    OnPropertyChanged(nameof(LoadRatio));
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
                    OnPropertyChanged(nameof(IsOverAddress));
                }
            }
        }

        public double LoadRatio => _maxLoadmA > 0 ? _loadmA / _maxLoadmA : 0.0;
        public double AddressRatio => _maxAddressCount > 0 ? (double)_addressCount / _maxAddressCount : 0.0;
        public bool IsOverLoad => LoadRatio > 1.0;
        public bool IsOverAddress => AddressRatio > 1.0;

        /// <summary>Called by GroupingViewModel after a successful Add to Line to update gauge values.</summary>
        public void UpdateGauges(AddToLineResult result)
        {
            LoadmA = result.TotalLoadmA;
            AddressCount = result.TotalAddressCount;
        }

        public ICommand AddToLineCommand { get; }
        public ICommand DeleteCommand { get; }
    }
}
