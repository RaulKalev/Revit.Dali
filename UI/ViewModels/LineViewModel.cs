using Dali.Models;
using Dali.Services.Revit;
using System;
using System.Windows.Input;
using System.Windows.Media;

namespace Dali.UI.ViewModels
{
    public class LineViewModel : BaseViewModel
    {
        private readonly LineDefinition _model;
        private readonly Action<LineViewModel> _addToLineAction;
        private readonly Action<LineViewModel> _deleteAction;
        private readonly Action<LineViewModel> _interactiveAddAction;
        private readonly Action<LineViewModel, string> _onNameChanged;
        private readonly Action<LineViewModel> _changeColorAction;
        private readonly Action<LineViewModel> _highlightAction;

        public LineViewModel(
            string panelName,
            string controllerModelName,
            LineDefinition model,
            Action<LineViewModel> addToLineAction,
                             Action<LineViewModel> deleteAction,
                             Action<LineViewModel> interactiveAddAction = null,
                             Action<LineViewModel, string> onNameChanged = null,
                             Action<LineViewModel> changeColorAction = null,
                             Action<LineViewModel> highlightAction = null)
        {
            _panelName = panelName ?? string.Empty;
            _controllerModelName = controllerModelName ?? string.Empty;
            _model = model ?? throw new ArgumentNullException(nameof(model));

            _addToLineAction = addToLineAction;
            _deleteAction = deleteAction;
            _interactiveAddAction = interactiveAddAction;
            _onNameChanged = onNameChanged;
            _changeColorAction = changeColorAction;
            _highlightAction = highlightAction;

            AddToLineCommand = new RelayCommand(_ => _addToLineAction?.Invoke(this));
            DeleteCommand = new RelayCommand(_ => _deleteAction?.Invoke(this));
            AddInteractiveCommand = new RelayCommand(_ => _interactiveAddAction?.Invoke(this));
            ChangeColorCommand = new RelayCommand(_ => _changeColorAction?.Invoke(this));
            HighlightCommand = new RelayCommand(_ => _highlightAction?.Invoke(this));
        }

        public ICommand AddToLineCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand AddInteractiveCommand { get; }
        public ICommand ChangeColorCommand { get; }
        public ICommand HighlightCommand { get; }

        public LineDefinition Model => _model;

        private string _panelName;
        public string PanelName 
        { 
            get => _panelName; 
            set => SetProperty(ref _panelName, value); 
        }

        private string _controllerModelName;
        public string ControllerModelName 
        { 
            get => _controllerModelName; 
            set => SetProperty(ref _controllerModelName, value); 
        }

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
                    _onNameChanged?.Invoke(this, oldName);
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
                    string oldName = _model.ControllerName;
                    _model.ControllerName = value;
                    OnPropertyChanged();
                    // Optional: could trigger a different event for moving lines, but for now we'll just use the same.
                    _onNameChanged?.Invoke(this, oldName);
                }
            }
        }

        public string ColorHex
        {
            get => _model.ColorHex;
            set
            {
                if (_model.ColorHex != value)
                {
                    _model.ColorHex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ColorBrush));
                }
            }
        }

        public SolidColorBrush ColorBrush
        {
            get
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(ColorHex)) return Brushes.White;
                    var convertFromString = ColorConverter.ConvertFromString(ColorHex);
                    if (convertFromString != null)
                    {
                        var color = (Color)convertFromString;
                        return new SolidColorBrush(color);
                    }
                }
                catch
                {
                    // Fallback on invalid hex
                }
                return Brushes.White;
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private double _loadmA;
        public double LoadmA
        {
            get => _loadmA;
            set
            {
                if (SetProperty(ref _loadmA, value))
                {
                    OnPropertyChanged(nameof(LoadRatio));
                    OnPropertyChanged(nameof(IsWarningLoad));
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

        public double LoadRatio => _maxLoadmA > 0 ? _loadmA / _maxLoadmA : 0.0;
        public double AddressRatio => _maxAddressCount > 0 ? (double)_addressCount / _maxAddressCount : 0.0;
        
        public bool IsWarningLoad => LoadRatio >= 0.8 && LoadRatio <= 1.0;
        public bool IsOverLoad => LoadRatio > 1.0;

        public bool IsWarningAddress => AddressRatio >= 0.8 && AddressRatio <= 1.0;
        public bool IsOverAddress => AddressRatio > 1.0;

        /// <summary>Called by GroupingViewModel after a successful Add to Line to update gauge values.</summary>
        public void UpdateGauges(AddToLineResult result)
        {
            LoadmA = result.TotalLoadmA;
            AddressCount = result.TotalAddressCount;
        }

        public void UpdateGaugesDelta(double loadDelta, int addressDelta)
        {
            LoadmA += loadDelta;
            AddressCount += addressDelta;
        }

    }
}


