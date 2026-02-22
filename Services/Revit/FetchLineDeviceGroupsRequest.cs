using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dali.Models;
using Dali.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dali.Services.Revit
{
    /// <summary>
    /// Carries the link between a line name and the visualization VM that should receive the groups.
    /// </summary>
    public class LineDeviceGroupInfo
    {
        public string LineName { get; set; }
        public DaliLineVizVm TargetVm { get; set; }
    }

    /// <summary>
    /// Read-only Revit scan: for each requested line, collect all assigned devices,
    /// group them by the user-configured grouping parameter, and push the results
    /// back into the DaliLineVizVm instances.
    ///
    /// Threading: Execute() runs on the Revit API thread, which is the UI thread
    /// for ExternalEvent. Callback is invoked directly (no Dispatcher needed).
    /// No document writes are performed.
    /// </summary>
    public class FetchLineDeviceGroupsRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly string _groupingParamName;
        private readonly List<LineDeviceGroupInfo> _lines;
        private readonly Action _completedCallback;

        public FetchLineDeviceGroupsRequest(
            SettingsModel settings,
            string groupingParamName,
            List<LineDeviceGroupInfo> lines,
            Action completedCallback = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _groupingParamName = groupingParamName ?? string.Empty;
            _lines = lines ?? new List<LineDeviceGroupInfo>();
            _completedCallback = completedCallback;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app?.ActiveUIDocument;
                if (uidoc == null) return;

                var doc = uidoc.Document;

                if (string.IsNullOrWhiteSpace(_settings.Param_LineId)) return;
                if (_lines.Count == 0) return;

                var catIds = new List<ElementId>();
                foreach (var bic in _settings.IncludedCategories)
                    catIds.Add(new ElementId(bic));
                if (catIds.Count == 0) return;

                var categoryFilter = new ElementMulticategoryFilter(catIds);
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(categoryFilter)
                    .WhereElementIsNotElementType();

                // lineName (lower) -> list of group values
                var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (Element elem in collector)
                {
                    if (elem is RevitLinkInstance) continue;

                    Parameter lineParam = elem.LookupParameter(_settings.Param_LineId);
                    if (lineParam == null) continue;

                    string lineName = lineParam.StorageType == StorageType.String
                        ? lineParam.AsString()
                        : lineParam.AsValueString();
                    if (string.IsNullOrWhiteSpace(lineName)) continue;

                    string lineKey = lineName.Trim();

                    string groupVal = "(Tühi)";
                    if (!string.IsNullOrWhiteSpace(_groupingParamName))
                    {
                        Parameter gp = elem.LookupParameter(_groupingParamName);
                        if (gp != null && gp.HasValue)
                        {
                            string v = gp.StorageType == StorageType.String
                                ? gp.AsString()
                                : gp.AsValueString();
                            groupVal = string.IsNullOrWhiteSpace(v) ? "(Tühi)" : v.Trim();
                        }
                    }

                    if (!grouped.ContainsKey(lineKey))
                        grouped[lineKey] = new List<string>();
                    grouped[lineKey].Add(groupVal);
                }

                // Push results into VMs — already on UI thread (ExternalEvent pattern).
                foreach (var li in _lines)
                {
                    if (li.TargetVm == null) continue;

                    string key = li.LineName?.Trim() ?? string.Empty;
                    if (!grouped.TryGetValue(key, out var vals)) continue;

                    var groups = vals
                        .GroupBy(v => v)
                        .Select(g => new DeviceGroupVizVm { Key = g.Key, Count = g.Count() })
                        .OrderByDescending(g => g.Count)
                        .ThenBy(g => g.Key)
                        .ToList();

                    li.TargetVm.Groups.Clear();
                    foreach (var g in groups)
                        li.TargetVm.Groups.Add(g);

                    li.TargetVm.DeviceCount = vals.Count;
                }

                _completedCallback?.Invoke();
            }
            catch (Exception ex)
            {
                App.Logger?.Error($"FetchLineDeviceGroupsRequest failed: {ex.Message}");
            }
        }
    }
}
