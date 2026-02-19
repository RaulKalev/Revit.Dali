using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dali.Models;
using System;
using System.Collections.Generic;

namespace Dali.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that scans ALL elements in the active document,
    /// groups them by their DALI_Line_ID instance parameter value, and
    /// accumulates load (mA) and address counts per line.
    /// 
    /// This is used on startup to populate line/controller gauges without
    /// requiring the user to manually select elements first.
    /// 
    /// Threading: Execute() runs on the Revit API thread.
    /// The callback is marshalled to the WPF UI thread via Dispatcher.Invoke.
    /// </summary>
    public class ScanModelTotalsRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly Action<ModelScanResult> _callback;

        public ScanModelTotalsRequest(SettingsModel settings, Action<ModelScanResult> callback)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Execute(UIApplication app)
        {
            var result = new ModelScanResult();

            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    result.Warnings.Add("No active document — cannot scan model.");
                    DispatchResult(result);
                    return;
                }

                var doc = uidoc.Document;

                // --- Build category filter ---
                var includedCategoryIds = new HashSet<int>();
                foreach (var bic in _settings.IncludedCategories)
                    includedCategoryIds.Add((int)bic);

                if (includedCategoryIds.Count == 0)
                {
                    result.Warnings.Add("No categories configured — skipping model scan.");
                    DispatchResult(result);
                    return;
                }

                string lineIdParamName = _settings.Param_LineId;
                if (string.IsNullOrWhiteSpace(lineIdParamName))
                {
                    result.Warnings.Add("DALI_Line_ID parameter not configured — skipping model scan.");
                    DispatchResult(result);
                    return;
                }

                // --- Type-level param cache: typeId -> (loadmA, addressCount) ---
                var typeCache = new Dictionary<long, CachedType>();

                // --- Collect all non-type elements in the document ---
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                App.Logger?.Info("ScanModelTotals: starting full document scan...");
                int scanned = 0, matched = 0;

                foreach (Element element in collector)
                {
                    scanned++;

                    // Skip type elements & links
                    if (element is ElementType || element is RevitLinkInstance)
                        continue;

                    // Category filter
                    Category cat = element.Category;
                    if (cat == null) continue;

#if NET48
                    int catIdInt = cat.Id.IntegerValue;
#else
                    int catIdInt = (int)cat.Id.Value;
#endif

                    if (!includedCategoryIds.Contains(catIdInt)) continue;

                    // Read DALI_Line_ID from instance
                    Parameter lineParam = element.LookupParameter(lineIdParamName);
                    if (lineParam == null) continue;

                    string lineIdValue = lineParam.AsString()?.Trim();
                    if (string.IsNullOrEmpty(lineIdValue)) continue;

                    // Read mA + addresses from type (cached)
                    ElementId typeId = element.GetTypeId();
#if NET48
                    if (typeId == null || typeId == ElementId.InvalidElementId) continue;
                    long typeKey = (long)typeId.IntegerValue;
#else
                    if (typeId == ElementId.InvalidElementId) continue;
                    long typeKey = typeId.Value;
#endif

                    if (!typeCache.TryGetValue(typeKey, out var cached))
                    {
                        cached = ReadTypeParams(doc, typeId);
                        typeCache[typeKey] = cached;
                    }

                    // Accumulate into the line bucket
                    if (!result.ByLine.TryGetValue(lineIdValue, out var totals))
                    {
                        totals = new ModelScanResult.LineTotals();
                        result.ByLine[lineIdValue] = totals;
                    }

                    totals.LoadmA += cached.LoadmA;
                    totals.AddressCount += cached.AddressCount;
                    totals.ElementCount++;
                    matched++;
                }

                App.Logger?.Info($"ScanModelTotals: scanned {scanned} elements, matched {matched} into {result.ByLine.Count} line(s).");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Error scanning model: {ex.Message}");
                App.Logger?.Error("ScanModelTotals: exception.", ex);
            }

            DispatchResult(result);
        }

        private CachedType ReadTypeParams(Document doc, ElementId typeId)
        {
            var cached = new CachedType();
            Element typeElement = doc.GetElement(typeId);
            if (typeElement is FamilySymbol symbol)
            {
                if (!string.IsNullOrWhiteSpace(_settings.Param_Load))
                {
                    Parameter p = symbol.LookupParameter(_settings.Param_Load);
                    if (p != null) cached.LoadmA = ReadDouble(p);
                }
                if (!string.IsNullOrWhiteSpace(_settings.Param_AddressCount))
                {
                    Parameter p = symbol.LookupParameter(_settings.Param_AddressCount);
                    if (p != null) cached.AddressCount = ReadInt(p);
                }
            }
            return cached;
        }

        private static double ReadDouble(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.Double: return p.AsDouble();
                case StorageType.Integer: return (double)p.AsInteger();
                case StorageType.String:
                    return double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0.0;
                default: return 0.0;
            }
        }

        private static int ReadInt(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.Integer: return p.AsInteger();
                case StorageType.Double: return (int)p.AsDouble();
                case StorageType.String:
                    return int.TryParse(p.AsString(), out int v) ? v : 0;
                default: return 0;
            }
        }

        private void DispatchResult(ModelScanResult result)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
                dispatcher.Invoke(() => _callback(result));
            else
                _callback(result);
        }

        private class CachedType
        {
            public double LoadmA { get; set; }
            public int AddressCount { get; set; }
        }
    }
}
