using System;
using Dali.UI.ViewModels;

namespace Dali.Services.Visualization
{
    /// <summary>
    /// Builds a ControllerVizVm from an existing ControllerViewModel.
    /// This is a pure read operation — no Revit API calls here.
    /// Device groups are populated separately via FetchLineDeviceGroupsRequest.
    /// </summary>
    public static class ControllerVisualizationBuilder
    {
        /// <summary>
        /// Build a visualization VM from the currently expanded ControllerViewModel.
        /// Returns null if ctrl is null.
        /// </summary>
        public static ControllerVizVm Build(ControllerViewModel ctrl)
        {
            if (ctrl == null) return null;

            int outputCount = ctrl.SelectedDevice?.DaliLines ?? ctrl.Lines.Count;
            outputCount = Math.Max(outputCount, ctrl.Lines.Count);

            var vm = new ControllerVizVm
            {
                PanelName = ctrl.PanelName ?? string.Empty,
                ControllerName = ctrl.Name ?? string.Empty,
                DeviceModel = ctrl.SelectedDevice?.Name ?? string.Empty,
                OutputCount = outputCount
            };

            // Map each line to a positional output (1-indexed).
            for (int i = 0; i < ctrl.Lines.Count; i++)
            {
                var line = ctrl.Lines[i];
                var outputVm = new ControllerOutputVizVm { OutputNumber = i + 1 };

                var lineVm = new DaliLineVizVm
                {
                    LineName = line.Name ?? $"Line {i + 1}",
                    DeviceCount = line.AddressCount,
                    MaxDevices = line.MaxAddressCount > 0 ? line.MaxAddressCount : 64,
                    LoadRatio    = line.MaxLoadmA    > 0 ? line.LoadmA    / line.MaxLoadmA    : 0,
                    AddressRatio = line.MaxAddressCount > 0 ? line.AddressCount / (double)line.MaxAddressCount : 0
                };

                outputVm.Lines.Add(lineVm);
                vm.Outputs.Add(outputVm);
            }

            // Pad remaining outputs with empty slots if device has more outputs than lines defined.
            for (int i = vm.Outputs.Count; i < outputCount; i++)
            {
                vm.Outputs.Add(new ControllerOutputVizVm { OutputNumber = i + 1 });
            }

            return vm;
        }
    }
}
