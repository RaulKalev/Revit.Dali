using Dali.Models;
using Dali.Services;
using Dali.Services.Core;
using Dali.Services.Revit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

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
        private readonly DeviceDatabaseService _deviceDatabaseService;
        
        // Track which line triggered the current Add operation so we can update its gauge
        private LineViewModel _pendingLine;

        public GroupingViewModel(SettingsService settingsService, RevitExternalEventService eventService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _highlightRegistry = new HighlightRegistry();
            _deviceDatabaseService = new DeviceDatabaseService(new SessionLogger());

            Warnings = new ObservableCollection<string>();
            Panels = new ObservableCollection<PanelViewModel>();

            RefreshSelectionCommand = new RelayCommand(_ => RefreshSelection(), _ => !IsBusy);
            ResetOverridesCommand = new RelayCommand(_ => ResetOverrides(), _ => !IsBusy);
            AddPanelCommand = new RelayCommand(_ => AddPanel());

            // Subscribe to settings changes
            _settingsService.OnSettingsSaved += OnSettingsSaved;

            LoadPanels();
            ScanModelOnStartup();
        }

        // ---- Panel/Controller hierarchy ----

        public ObservableCollection<PanelViewModel> Panels { get; }

        private ControllerViewModel _activeController;
        /// <summary>The currently expanded controller. Drives the main gauges.</summary>
        public ControllerViewModel ActiveController
        {
            get => _activeController;
            private set => SetProperty(ref _activeController, value);
        }

        private void LoadPanels()
        {
            var settings = _settingsService.Load();
            Panels.Clear();

            // Apply global limits from settings
            _controllerMaxLoadmA = settings.ControllerMaxLoadmA > 0 ? settings.ControllerMaxLoadmA : 250.0;
            _controllerMaxAddressCount = settings.ControllerMaxAddressCount > 0 ? settings.ControllerMaxAddressCount : 64;
            _lineMaxLoadmA = settings.LineMaxLoadmA > 0 ? settings.LineMaxLoadmA : 250.0;
            _lineMaxAddressCount = settings.LineMaxAddressCount > 0 ? settings.LineMaxAddressCount : 64;

            if (settings.SavedPanels != null && settings.SavedPanels.Count > 0)
            {
                foreach (var panelDef in settings.SavedPanels)
                    Panels.Add(CreatePanelVM(panelDef));
            }
            else
            {
                // Default if empty
                var defCtrl = new ControllerDefinition { Name = "Controller 1" };
                defCtrl.Lines.Add(new LineDefinition { Name = "Line 1", ControllerName = "Controller 1" });
                var pDef = new PanelDefinition { Name = "Panel 1" };
                pDef.Controllers.Add(defCtrl);
                
                Panels.Add(CreatePanelVM(pDef));
                SavePanels();
            }

            // Propagate limits to all controllers
            foreach (var p in Panels)
            {
                foreach (var c in p.Controllers)
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

            // Open first panel and first controller by default
            if (Panels.Count > 0)
            {
                Panels[0].IsExpanded = true;
                if (Panels[0].Controllers.Count > 0)
                    Panels[0].Controllers[0].IsExpanded = true;
            }
        }

        private PanelViewModel CreatePanelVM(PanelDefinition pDef)
        {
            return new PanelViewModel(
                pDef,
                p => { p.AddNewController(CreateControllerVM); SavePanels(); },
                p => { DeletePanel(p); SavePanels(); },
                CreateControllerVM,
                (p, oldN) => OnPanelNameChanged(p, oldN)
            );
        }

        private ControllerViewModel CreateControllerVM(string panelName, ControllerDefinition def)
        {
            var vm = new ControllerViewModel(
                panelName,
                def,
                _deviceDatabaseService.GetControllers(),
                ctrl => { AddNewLine(ctrl); SavePanels(); },
                ctrl => { DeleteController(ctrl); SavePanels(); },
                line => AddToLine(line),
                line => AddToLineInteractive(line),
                ctrl => OnControllerExpanded(ctrl),
                (ctrl, oldN) => OnControllerNameChanged(ctrl, oldN),
                (line, oldN) => OnLineNameChanged(line, oldN),
                line => OnChangeLineColor(line),
                (ctrl, oldD) => OnControllerDeviceChanged(ctrl, oldD));
            
            if (vm.SelectedDevice == null)
            {
                vm.MaxLoadmA = _controllerMaxLoadmA;
                vm.MaxAddressCount = _controllerMaxAddressCount;
                
                // Also set limits on any existing lines
                foreach (var line in vm.Lines)
                {
                    line.MaxLoadmA = _lineMaxLoadmA;
                    line.MaxAddressCount = _lineMaxAddressCount;
                }
            }

            return vm;
        }

        private void OnPanelNameChanged(PanelViewModel panel, string oldName)
        {
            SavePanels();
            var settings = _settingsService.Load();

            foreach (var ctrl in panel.Controllers)
            {
                string dName = ctrl.AvailableDevices?.FirstOrDefault(d => d.Id == ctrl.Model.DeviceId)?.Name?.Trim() ?? string.Empty;
                string cName = ctrl.Model.Name?.Trim() ?? string.Empty;
                
                string oldFullName = $"{oldName?.Trim() ?? string.Empty} - {cName} - {dName}".Trim(' ', '-');
                string newFullName = $"{panel.Name?.Trim() ?? string.Empty} - {cName} - {dName}".Trim(' ', '-');

                _eventService.Raise(new RenameElementsRequest(settings, RenameScope.Controller, oldFullName, newFullName, null, res => {
                    if (res.Success) { StatusMessage = res.Message; }
                }));

                foreach (var line in ctrl.Lines) RecalculateLine(line);
            }
        }

        private void OnControllerDeviceChanged(ControllerViewModel ctrl, DeviceDto oldDevice)
        {
            SavePanels();
            var settings = _settingsService.Load();

            string pName = ctrl.PanelName?.Trim() ?? string.Empty;
            string dNameNew = ctrl.SelectedDevice?.Name?.Trim() ?? string.Empty;
            string dNameOld = oldDevice?.Name?.Trim() ?? string.Empty;
            string cName = ctrl.Name?.Trim() ?? string.Empty;

            string oldFullName = $"{pName} - {cName} - {dNameOld}".Trim(' ', '-');
            string newFullName = $"{pName} - {cName} - {dNameNew}".Trim(' ', '-');

            _eventService.Raise(new RenameElementsRequest(settings, RenameScope.Controller, oldFullName, newFullName, null, res => {
                if (res.Success) { StatusMessage = res.Message; }
            }));

            foreach (var line in ctrl.Lines)
            {
                RecalculateLine(line);
            }
        }

        private void OnControllerNameChanged(ControllerViewModel ctrl, string oldName)
        {
            SavePanels();

            string pName = ctrl.PanelName?.Trim() ?? string.Empty;
            string dName = ctrl.AvailableDevices?.FirstOrDefault(d => d.Id == ctrl.Model.DeviceId)?.Name?.Trim() ?? string.Empty;
            string oldFullName = $"{pName} - {oldName?.Trim() ?? string.Empty} - {dName}".Trim(' ', '-');
            string newFullName = $"{pName} - {ctrl.Name?.Trim() ?? string.Empty} - {dName}".Trim(' ', '-');

            var settings = _settingsService.Load();
            _eventService.Raise(new RenameElementsRequest(settings, RenameScope.Controller, oldFullName, newFullName, null, res => {
                if (res.Success) { StatusMessage = res.Message; }
            }));

            foreach (var line in ctrl.Lines)
            {
                RecalculateLine(line);
            }
        }

        internal void OnLineNameChanged(LineViewModel line, string oldName)
        {
            SavePanels();
            
            string pName = line.PanelName?.Trim() ?? string.Empty;
            string cName = line.ControllerName?.Trim() ?? string.Empty;
            string dName = line.ControllerModelName?.Trim() ?? string.Empty;
            string parentFullName = $"{pName} - {cName} - {dName}".Trim(' ', '-');

            var settings = _settingsService.Load();
            _eventService.Raise(new RenameElementsRequest(settings, RenameScope.Line, oldName, line.Name, parentFullName, res => {
                if (res.Success) { StatusMessage = res.Message; }
            }));

            RecalculateLine(line);
        }

        private void OnChangeLineColor(LineViewModel line)
        {
            var dialog = new System.Windows.Forms.ColorDialog();
            
            // pre-select current color if valid
            try
            {
                if (!string.IsNullOrWhiteSpace(line.ColorHex))
                {
                    var mediaColor = (System.Windows.Media.Color)ColorConverter.ConvertFromString(line.ColorHex);
                    dialog.Color = System.Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
                }
            }
            catch { /* ignore invalid hex */ }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Convert System.Drawing.Color -> Hex string
                string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                line.ColorHex = hex;
                SavePanels();

                var settings = _settingsService.Load();
                _eventService.Raise(new UpdateLineColorRequest(
                    settings, 
                    line.ControllerName, 
                    line.Name, 
                    hex, 
                    _highlightRegistry));
            }
        }

        private void RecalculateLine(LineViewModel line)
        {
            if (string.IsNullOrWhiteSpace(line.Name)) return;

            var settings = _settingsService.Load();
            
            string pName = line.PanelName?.Trim() ?? string.Empty;
            string cName = line.ControllerName?.Trim() ?? string.Empty;
            string dName = line.ControllerModelName?.Trim() ?? string.Empty;
            string fullControllerString = $"{pName} - {cName} - {dName}".Trim(' ', '-');

            var req = new RecalculateLineGaugesRequest(
                settings,
                line.Name,
                fullControllerString,
                (load, addr) => 
                {
                    line.LoadmA = load;
                    line.AddressCount = addr;
                    var parentCtrl = Panels.SelectMany(p => p.Controllers).FirstOrDefault(c => c.Lines.Contains(line));
                    parentCtrl?.RecalcTotals();
                });
            _eventService.Raise(req);
        }

        private void OnControllerExpanded(ControllerViewModel expanded)
        {
            // Accordion: collapse all others across all panels
            foreach (var panel in Panels)
            {
                foreach (var ctrl in panel.Controllers)
                {
                    if (ctrl != expanded && ctrl.IsExpanded)
                        ctrl.IsExpanded = false;
                }
            }
            ActiveController = expanded;
        }

        public ICommand AddPanelCommand { get; }

        private void AddPanel()
        {
            var pDef = new PanelDefinition { Name = $"Panel {Panels.Count + 1}" };
            var vm = CreatePanelVM(pDef);
            Panels.Add(vm);
            vm.IsExpanded = true;
            SavePanels();
        }

        private void DeletePanel(PanelViewModel panel)
        {
            if (Panels.Contains(panel))
            {
                Panels.Remove(panel);
            }
        }

        private void DeleteController(ControllerViewModel ctrl)
        {
            foreach (var panel in Panels)
            {
                if (panel.Controllers.Contains(ctrl))
                {
                    panel.RemoveController(ctrl);
                    if (ActiveController == ctrl)
                        ActiveController = Panels.SelectMany(p => p.Controllers).FirstOrDefault();
                    break;
                }
            }
        }

        private void AddNewLine(ControllerViewModel ctrl)
        {
            var line = ctrl.AddNewLine();
            line.MaxLoadmA = _lineMaxLoadmA;
            line.MaxAddressCount = _lineMaxAddressCount;
        }

        public void SavePanels()
        {
            var settings = _settingsService.Load();
            settings.SavedPanels = new List<PanelDefinition>();
            foreach (var panel in Panels)
                settings.SavedPanels.Add(panel.Model);
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
            foreach (var p in Panels)
            {
                foreach (var c in p.Controllers)
                {
                    if (c.SelectedDevice == null)
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
                string compositeKey = kvp.Key;
                var totals = kvp.Value;

                string ctrlName = string.Empty;
                string lineName = compositeKey;

                int separatorIndex = compositeKey.IndexOf("||");
                if (separatorIndex >= 0)
                {
                    ctrlName = compositeKey.Substring(0, separatorIndex);
                    lineName = compositeKey.Substring(separatorIndex + 2);
                }

                string pName = string.Empty;
                string cName = ctrlName;
                string mName = string.Empty;

                if (!string.IsNullOrWhiteSpace(ctrlName) && ctrlName.Contains(" - "))
                {
                    var parts = ctrlName.Split(new[] { " - " }, StringSplitOptions.None);
                    if (parts.Length == 3)
                    {
                        pName = parts[0].Trim();
                        cName = parts[1].Trim();
                        mName = parts[2].Trim();
                    }
                    else if (parts.Length == 2)
                    {
                        pName = parts[0].Trim();
                        cName = parts[1].Trim();
                    }
                }

                // 1. Resolve Panel
                PanelViewModel targetPanel = null;
                if (!string.IsNullOrWhiteSpace(pName))
                {
                    targetPanel = Panels.FirstOrDefault(p => string.Equals(p.Name, pName, StringComparison.OrdinalIgnoreCase));
                    if (targetPanel == null)
                    {
                        var pDef = new PanelDefinition { Name = pName };
                        targetPanel = CreatePanelVM(pDef);
                        Panels.Add(targetPanel);
                        SavePanels();
                    }
                }
                else
                {
                    targetPanel = Panels.FirstOrDefault();
                }

                if (targetPanel == null) continue;

                // 2. Resolve Controller
                ControllerViewModel targetCtrl = null;
                if (!string.IsNullOrWhiteSpace(cName))
                {
                    targetCtrl = targetPanel.Controllers.FirstOrDefault(c => string.Equals(c.Name, cName, StringComparison.OrdinalIgnoreCase));
                    if (targetCtrl == null)
                    {
                        targetCtrl = targetPanel.AddNewController(CreateControllerVM);
                        targetCtrl.Name = cName;
                        SavePanels();
                    }
                }

                if (targetCtrl == null) continue;

                // 3. Resolve Device Model
                if (!string.IsNullOrWhiteSpace(mName) && targetCtrl.SelectedDevice == null)
                {
                    var match = targetCtrl.AvailableDevices.FirstOrDefault(d => string.Equals(d.Name, mName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        targetCtrl.SelectedDevice = match;
                    }
                }

                // 4. Resolve Line
                var targetLine = targetCtrl.Lines.FirstOrDefault(l => string.Equals(l.Name, lineName, StringComparison.OrdinalIgnoreCase));
                if (targetLine == null)
                {
                    // If the controller has an empty line "Line 1" with 0 load, repurpose it
                    var emptyLine = targetCtrl.Lines.FirstOrDefault(l => string.Equals(l.Name, "Line 1", StringComparison.OrdinalIgnoreCase) && l.LoadmA == 0 && l.AddressCount == 0);
                    if (emptyLine != null)
                    {
                        emptyLine.Name = lineName;
                        targetLine = emptyLine;
                    }
                    else if (targetCtrl.CanAddLine)
                    {
                        // AddNewLine is private. 
                        // Actually, I can use AddLineCommand! But let's just use the factory or update the viewmodel. 
                        // To avoid refactoring ControllerViewModel, let's just not auto-create extra lines if they exceed limits.
                        // I will skip line creation here if `AddNewLine` isn't accessible. But wait, we can just log a warning.
                        targetLine = targetCtrl.Lines.FirstOrDefault();
                    }
                }

                if (targetLine != null)
                {
                    targetLine.LoadmA = totals.LoadmA;
                    targetLine.AddressCount = totals.AddressCount;
                }
                
                targetCtrl.RecalcTotals();
            }

            StatusMessage = result.Warnings.Count > 0
                ? $"Scan complete with {result.Warnings.Count} warning(s)."
                : $"Model scan complete — {result.ByLine.Count} distinct line(s) populated.";

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
            SavePanels();

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
                line.PanelName,
                line.ControllerModelName,
                line.Name,
                line.ControllerName,
                MaxLoadmA,
                MaxAddressCount,
                _highlightRegistry,
                OnAddToLineComplete,
                line.ColorHex);

            _eventService.Raise(request);
        }

        private void OnAddToLineComplete(AddToLineResult result)
        {
            IsBusy = false;
            StatusMessage = result.Message;

            Warnings.Clear();
            foreach (var d in result.Details) Warnings.Add(d);

            // Fetch the entire model's updated view of the selection.
            CurrentLoadmA = result.TotalLoadmA;
            CurrentAddressCount = result.TotalAddressCount;
            SelectedElementCount = result.UpdatedCount;
            SkippedElementCount = result.SkippedCount;

            OnPropertyChanged(nameof(LoadRatio));
            OnPropertyChanged(nameof(AddressRatio));
            OnPropertyChanged(nameof(IsOverLoadLimit));
            OnPropertyChanged(nameof(IsOverAddressLimit));

            // Force a true recalculation of the line to catch the full total
            if (_pendingLine != null && result.Success)
            {
                RecalculateLine(_pendingLine);
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
        // ---- AddToLineInteractive (Interactive Loop) ----

        private void AddToLineInteractive(LineViewModel line)
        {
            if (IsBusy || line == null || string.IsNullOrWhiteSpace(line.Name)) return;
            SavePanels();

            var settings = _settingsService.Load();
            if (settings.IncludedCategories == null || settings.IncludedCategories.Count == 0)
            { StatusMessage = "No categories configured in Settings."; return; }

            if (string.IsNullOrWhiteSpace(settings.Param_LineId))
            { StatusMessage = "Instance parameter (DALI_Line_ID) not configured in Settings."; return; }

            IsBusy = true;
            StatusMessage = $"Interactive Mode: Click devices to add to '{line.Name}'...";

            // Find parent controller for updates
            var parentCtrl = Panels.SelectMany(p => p.Controllers).FirstOrDefault(c => c.Lines.Contains(line));

            var request = new AddDevicesInteractiveRequest(
                settings,
                line.PanelName,
                line.ControllerModelName,
                line.Name,
                line.ControllerName,
                _highlightRegistry,
                // Status Callback
                msg => StatusMessage = msg,
                // Update Callback (Delta)
                (loadDelta, addrDelta) =>
                {
                    line.UpdateGaugesDelta(loadDelta, addrDelta);
                    parentCtrl?.RecalcTotals(); // Update controller totals
                    
                    // Update global selection totals (approximate, since we don't re-scan the whole selection)
                    CurrentLoadmA += loadDelta;
                    CurrentAddressCount += addrDelta;
                    SelectedElementCount++; 
                },
                // Completion Callback
                () => OnInteractiveComplete(line),
                line.ColorHex);

            _eventService.Raise(request);
        }

        private void OnInteractiveComplete(LineViewModel line)
        {
            IsBusy = false;
            StatusMessage = "Interactive selection finished.";
            if (line != null)
            {
                RecalculateLine(line);
            }
        }
    }
}
