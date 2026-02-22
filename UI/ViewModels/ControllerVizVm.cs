using System.Collections.ObjectModel;

namespace Dali.UI.ViewModels
{
    /// <summary>Device group node in the schematic (e.g. "V1D" circle "5 tk.").</summary>
    public class DeviceGroupVizVm : BaseViewModel
    {
        private string _key;
        public string Key
        {
            get => _key;
            set { SetProperty(ref _key, value); OnPropertyChanged(nameof(CountLabel)); }
        }

        private int _count;
        public int Count
        {
            get => _count;
            set { SetProperty(ref _count, value); OnPropertyChanged(nameof(CountLabel)); }
        }

        /// <summary>Shown to the right of the circle node, e.g. "5 tk."</summary>
        public string CountLabel => $"{Count} tk.";

        /// <summary>Kept for backwards compatibility.</summary>
        public string DisplayText => $"{Key} — {Count} tk.";
    }

    /// <summary>Represents one DALI line under a controller output.</summary>
    public class DaliLineVizVm : BaseViewModel
    {
        private string _lineName;
        public string LineName
        {
            get => _lineName;
            set => SetProperty(ref _lineName, value);
        }

        private int _deviceCount;
        public int DeviceCount
        {
            get => _deviceCount;
            set { SetProperty(ref _deviceCount, value); OnPropertyChanged(nameof(DeviceCountDisplay)); }
        }

        private int _maxDevices = 64;
        public int MaxDevices
        {
            get => _maxDevices;
            set { SetProperty(ref _maxDevices, value); OnPropertyChanged(nameof(DeviceCountDisplay)); }
        }

        public string DeviceCountDisplay => $"{DeviceCount} / {MaxDevices}";

        public ObservableCollection<DeviceGroupVizVm> Groups { get; } = new ObservableCollection<DeviceGroupVizVm>();
    }

    /// <summary>One output column in the schematic — output number and its assigned line.</summary>
    public class ControllerOutputVizVm : BaseViewModel
    {
        private int _outputNumber;
        public int OutputNumber
        {
            get => _outputNumber;
            set { SetProperty(ref _outputNumber, value); OnPropertyChanged(nameof(OutputLabel)); }
        }

        public string OutputLabel => $"OUT{OutputNumber}";

        /// <summary>Collection of DALI lines on this output (usually 0 or 1).</summary>
        public ObservableCollection<DaliLineVizVm> Lines { get; } = new ObservableCollection<DaliLineVizVm>();

        /// <summary>The first (and normally only) DALI line on this output. Null if none assigned.</summary>
        public DaliLineVizVm FirstLine => Lines.Count > 0 ? Lines[0] : null;
    }

    /// <summary>Top-level controller visualization — panel name, controller name, outputs.</summary>
    public class ControllerVizVm : BaseViewModel
    {
        private string _panelName;
        public string PanelName
        {
            get => _panelName;
            set => SetProperty(ref _panelName, value);
        }

        private string _controllerName;
        public string ControllerName
        {
            get => _controllerName;
            set => SetProperty(ref _controllerName, value);
        }

        private string _deviceModel;
        public string DeviceModel
        {
            get => _deviceModel;
            set => SetProperty(ref _deviceModel, value);
        }

        private int _outputCount;
        public int OutputCount
        {
            get => _outputCount;
            set => SetProperty(ref _outputCount, value);
        }

        public ObservableCollection<ControllerOutputVizVm> Outputs { get; } = new ObservableCollection<ControllerOutputVizVm>();
    }
}

