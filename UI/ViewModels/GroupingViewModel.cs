using Dali.Models;
using Dali.Services;
using Dali.Services.Revit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace Dali.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Grouping tab.
    /// Manages a hierarchical list of ControllerViewModels (accordion: only one open at a time).
    /// The main gauges at the top reflect the currently expanded controller's totals.
    /// </summary>
    public class GroupingViewModel : BaseViewModel
    {
        private readonly SettingsService _settingsService;
        private readonly RevitExternalEventService _eventService;
        private readonly HighlightRegistry _highlightRegistry;
        
        // Track which line triggered the current Add operation so we can update its gauge
        private LineViewModel _pendingLine;

        public GroupingViewModel(SettingsService settingsService, RevitExternalEventService eventService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _highlightRegistry = new HighlightRegistry();

            Warnings = new ObservableCollection<string>();
            Controllers = new ObservableCollection<ControllerViewModel>();

            RefreshSelectionCommand = new RelayCommand(_ => RefreshSelection(), _ => !IsBusy);
            ResetOverridesCommand = new RelayCommand(_ => ResetOverrides(), _ => !IsBusy);
            AddControllerCommand = new RelayCommand(_ => AddController());

            // Subscribe to settings changes
            _settingsService.OnSettingsSaved += OnSettingsSaved;

            LoadControllers();
            ScanModelOnStartup();
        }

        // ---- Controller hierarchy ----

        public ObservableCollection<ControllerViewModel> Controllers { get; }

        private ControllerViewModel _activeController;
        /// <summary>The currently expanded controller. Drives the main gauges.</summary>
        public ControllerViewModel ActiveController
        {
            get => _activeController;
            private set => SetProperty(ref _activeController, value);
        }

        private void LoadControllers()
        {
            var settings = _settingsService.Load();
            Controllers.Clear();

            // Apply global limits from settings
            _controllerMaxLoadmA = settings.ControllerMaxLoadmA > 0 ? settings.ControllerMaxLoadmA : 250.0;
            _controllerMaxAddressCount = settings.ControllerMaxAddressCount > 0 ? settings.ControllerMaxAddressCount : 64;
            _lineMaxLoadmA = settings.LineMaxLoadmA > 0 ? settings.LineMaxLoadmA : 250.0;
            _lineMaxAddressCount = settings.LineMaxAddressCount > 0 ? settings.LineMaxAddressCount : 64;

            if (settings.SavedControllers != null)
            {
                foreach (var ctrlDef in settings.SavedControllers)
                    Controllers.Add(CreateControllerVM(ctrlDef));
            }

            // Default if empty
            if (Controllers.Count == 0)
            {
                var def = new ControllerDefinition { Name = "Controller 1" };
                def.Lines.Add(new LineDefinition { Name = "Line 1", ControllerName = "Controller 1" });
                Controllers.Add(CreateControllerVM(def));
                SaveControllers();
            }

            // Propagate limits to all controllers
            foreach (var c in Controllers)
            {
                c.MaxLoadmA = _controllerMaxLoadmA;
                c.MaxAddressCount = _controllerMaxAddressCount;
                foreach (var line in c.Lines)
                {
                    line.MaxLoadmA = _lineMaxLoadmA;
                    line.MaxAddressCount = _lineMaxAddressCount;
                }
            }

            // Open first controller by default
            if (Controllers.Count > 0)
                Controllers[0].IsExpanded = true;
        }

        private ControllerViewModel CreateControllerVM(ControllerDefinition def)
        {
            var vm = new ControllerViewModel(
                def,
                ctrl => { AddNewLine(ctrl); SaveControllers(); },
                ctrl => { DeleteController(ctrl); SaveControllers(); },
                line => AddToLine(line),
                ctrl => OnControllerExpanded(ctrl));
            
            vm.MaxLoadmA = _controllerMaxLoadmA;
            vm.MaxAddressCount = _controllerMaxAddressCount;
            
            // Also set limits on any existing lines
            foreach (var line in vm.Lines)
            {
                line.MaxLoadmA = _lineMaxLoadmA;
                line.MaxAddressCount = _lineMaxAddressCount;
            }

            return vm;
        }

        private void OnControllerExpanded(ControllerViewModel expanded)
        {
            // Accordion: collapse all others
            foreach (var ctrl in Controllers)
            {
                if (ctrl != expanded && ctrl.IsExpanded)
                    ctrl.IsExpanded = false;
            }
            ActiveController = expanded;
        }

        public ICommand AddControllerCommand { get; }

        private void AddController()
        {
            var def = new ControllerDefinition { Name = $"Controller {Controllers.Count + 1}" };
            def.Lines.Add(new LineDefinition { Name = "Line 1", ControllerName = def.Name });
            var vm = CreateControllerVM(def);
            Controllers.Add(vm);
            vm.IsExpanded = true; // triggers accordion via OnControllerExpanded
            SaveControllers();
        }

        private void DeleteController(ControllerViewModel ctrl)
        {
            if (Controllers.Contains(ctrl))
            {
                Controllers.Remove(ctrl);
                if (ActiveController == ctrl)
                    ActiveController = Controllers.FirstOrDefault();
            }
        }

        private void AddNewLine(ControllerViewModel ctrl)
        {
            var line = ctrl.AddNewLine();
            line.MaxLoadmA = _lineMaxLoadmA;
            line.MaxAddressCount = _lineMaxAddressCount;
        }

        public void SaveControllers()
        {
            var settings = _settingsService.Load();
            settings.SavedControllers = new List<ControllerDefinition>();
            foreach (var ctrl in Controllers)
                settings.SavedControllers.Add(ctrl.Model);
            _settingsService.Save(settings);
        }

        private void OnSettingsSaved(object sender, SettingsModel settings)
        {
            // Update local fields
            _controllerMaxLoadmA = settings.ControllerMaxLoadmA > 0 ? settings.ControllerMaxLoadmA : 250.0;
            _controllerMaxAddressCount = settings.ControllerMaxAddressCount > 0 ? settings.ControllerMaxAddressCount : 64;
            _lineMaxLoadmA = settings.LineMaxLoadmA > 0 ? settings.LineMaxLoadmA : 250.0;
            _lineMaxAddressCount = settings.LineMaxAddressCount > 0 ? settings.LineMaxAddressCount : 64;

            // Notify dashboard properties changed (as they use these fields)
            OnPropertyChanged(nameof(MaxLoadmA));
            OnPropertyChanged(nameof(MaxAddressCount));
            // Trigger recalculation of dashboard ratios
            OnPropertyChanged(nameof(LoadRatio));
            OnPropertyChanged(nameof(AddressRatio));
            OnPropertyChanged(nameof(IsOverLoadLimit));
            OnPropertyChanged(nameof(IsOverAddressLimit));

            // Propagate to all existing controllers and lines
            foreach (var c in Controllers)
            {
                c.MaxLoadmA = _controllerMaxLoadmA;
                c.MaxAddressCount = _controllerMaxAddressCount;
                foreach (var line in c.Lines)
                {
                    line.MaxLoadmA = _lineMaxLoadmA;
                    line.MaxAddressCount = _lineMaxAddressCount;
                }
            }
        }

        // ---- Startup model scan ----

        /// <summary>
        /// Fires a Revit scan request that reads all existing DALI elements and
        /// pre-populates the line/controller gauge values from the model.
        /// </summary>
        private void ScanModelOnStartup()
        {
            var settings = _settingsService.Load();
            if (settings.IncludedCategories == null || settings.IncludedCategories.Count == 0) return;
            if (string.IsNullOrWhiteSpace(settings.Param_LineId)) return;

            StatusMessage = "Scanning model for existing assignments...";
            _eventService.Raise(new ScanModelTotalsRequest(settings, OnScanComplete));
        }

        private void OnScanComplete(ModelScanResult result)
        {
            foreach (var kvp in result.ByLine)
            {
                string lineName = kvp.Key;
                var totals = kvp.Value;

                // Find the matching LineViewModel across all controllers
                foreach (var ctrl in Controllers)
                {
                    foreach (var line in ctrl.Lines)
                    {
                        if (string.Equals(line.Name?.Trim(), lineName, StringComparison.OrdinalIgnoreCase))
                        {
                            line.LoadmA = totals.LoadmA;
                            line.AddressCount = totals.AddressCount;
                        }
                    }
                    ctrl.RecalcTotals();
                }
            }

            StatusMessage = result.Warnings.Count > 0
                ? $"Scan complete with {result.Warnings.Count} warning(s)."
                : $"Model scan complete — {result.ByLine.Count} line(s) populated.";

            foreach (var w in result.Warnings)
            {
                if (!Warnings.Contains(w)) Warnings.Add(w);
            }
        }

        // ---- Limits (global, propagated to all controllers) ----

        private double _controllerMaxLoadmA;
        private int _controllerMaxAddressCount;
        private double _lineMaxLoadmA;
        private int _lineMaxAddressCount;

        // Exposed properties for the dashboard gauges (Active Selection)
        // We probably want to compare selection against LINE limits, as selection usually goes into a line.
        public double MaxLoadmA => _lineMaxLoadmA;
        public int MaxAddressCount => _lineMaxAddressCount;

        // ---- Selection totals (from Revit refresh) ----

        private double _currentLoadmA;
        public double CurrentLoadmA
        {
            get => _currentLoadmA;
            set
            {
                if (SetProperty(ref _currentLoadmA, value))
                {
                    OnPropertyChanged(nameof(LoadRatio));
                    OnPropertyChanged(nameof(IsOverLoadLimit));
                }
            }
        }

        private int _currentAddressCount;
        public int CurrentAddressCount
        {
            get => _currentAddressCount;
            set
            {
                if (SetProperty(ref _currentAddressCount, value))
                {
                    OnPropertyChanged(nameof(AddressRatio));
                    OnPropertyChanged(nameof(IsOverAddressLimit));
                }
            }
        }

        private int _selectedElementCount;
        public int SelectedElementCount
        {
            get => _selectedElementCount;
            set => SetProperty(ref _selectedElementCount, value);
        }

        private int _skippedElementCount;
        public int SkippedElementCount
        {
            get => _skippedElementCount;
            set => SetProperty(ref _skippedElementCount, value);
        }

        public double LoadRatio => MaxLoadmA > 0 ? CurrentLoadmA / MaxLoadmA : 0.0;
        public double AddressRatio => MaxAddressCount > 0 ? (double)CurrentAddressCount / MaxAddressCount : 0.0;
        public bool IsOverLoadLimit => LoadRatio > 1.0;
        public bool IsOverAddressLimit => AddressRatio > 1.0;

        // ---- Warnings / Status ----

        public ObservableCollection<string> Warnings { get; }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _statusMessage = "Select elements in Revit, then click 'Refresh Selection'.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ---- Commands ----

        public ICommand RefreshSelectionCommand { get; }
        public ICommand ResetOverridesCommand { get; }

        // ---- RefreshSelection ----

        private void RefreshSelection()
        {
            var settings = _settingsService.Load();
            if (settings.IncludedCategories == null || settings.IncludedCategories.Count == 0)
            {
                StatusMessage = "No categories configured in Settings.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Reading selection...";
            _eventService.Raise(new RefreshSelectionTotalsRequest(settings, OnRefreshComplete));
        }

        private void OnRefreshComplete(SelectionTotalsResult result)
        {
            IsBusy = false;
            CurrentLoadmA = result.TotalLoadmA;
            CurrentAddressCount = result.TotalAddressCount;
            SelectedElementCount = result.ValidElementCount;
            SkippedElementCount = result.SkippedElementCount;

            Warnings.Clear();
            foreach (var w in result.Warnings) Warnings.Add(w);

            StatusMessage = (result.ValidElementCount == 0 && result.SkippedElementCount == 0)
                ? "No elements selected."
                : $"Valid: {result.ValidElementCount} | Skipped: {result.SkippedElementCount}";
        }

        // ---- AddToLine (per-line, from card button) ----

        private void AddToLine(LineViewModel line)
        {
            if (IsBusy || line == null || string.IsNullOrWhiteSpace(line.Name)) return;
            SaveControllers();

            var settings = _settingsService.Load();
            if (settings.IncludedCategories == null || settings.IncludedCategories.Count == 0)
            { StatusMessage = "No categories configured in Settings."; return; }

            if (string.IsNullOrWhiteSpace(settings.Param_LineId))
            { StatusMessage = "Instance parameter (DALI_Line_ID) not configured in Settings."; return; }

            IsBusy = true;
            StatusMessage = $"Assigning selection to '{line.Name}'...";
            _pendingLine = line;

            var request = new AddToLineRequest(
                settings,
                line.Name,
                line.ControllerName,
                MaxLoadmA,
                MaxAddressCount,
                _highlightRegistry,
                OnAddToLineComplete);

            _eventService.Raise(request);
        }

        private void OnAddToLineComplete(AddToLineResult result)
        {
            IsBusy = false;
            StatusMessage = result.Message;

            Warnings.Clear();
            foreach (var d in result.Details) Warnings.Add(d);

            // Update current selection totals
            CurrentLoadmA = result.TotalLoadmA;
            CurrentAddressCount = result.TotalAddressCount;
            SelectedElementCount = result.UpdatedCount;
            SkippedElementCount = result.SkippedCount;

            OnPropertyChanged(nameof(LoadRatio));
            OnPropertyChanged(nameof(AddressRatio));
            OnPropertyChanged(nameof(IsOverLoadLimit));
            OnPropertyChanged(nameof(IsOverAddressLimit));

            // Update per-line and per-controller gauges
            if (_pendingLine != null && result.Success)
            {
                // Find the parent controller
                var parentCtrl = Controllers.FirstOrDefault(c => c.Lines.Contains(_pendingLine));
                parentCtrl?.OnLineAddComplete(_pendingLine, result);
            }
            _pendingLine = null;
        }

        // ---- ResetOverrides ----

        private void ResetOverrides()
        {
            IsBusy = true;
            StatusMessage = "Resetting overrides...";
            _eventService.Raise(new ResetOverridesRequest(_highlightRegistry, OnResetComplete));
        }

        private void OnResetComplete(ResetResult result)
        {
            IsBusy = false;
            StatusMessage = result.Message;
            if (!result.Success) { Warnings.Clear(); Warnings.Add(result.Message); }
        }
    }
}
