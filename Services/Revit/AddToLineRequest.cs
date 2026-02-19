using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dali.Models;
using Dali.Services.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dali.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that writes the ActiveLineName to the DALI_Line_ID
    /// instance parameter on all valid selected elements, with strict limit validation.
    /// 
    /// Threading: Execute() runs on the Revit API thread.
    /// The callback is marshalled to the WPF UI thread via Dispatcher.Invoke.
    /// 
    /// Safety: Re-computes totals inside the handler (does NOT trust UI values)
    /// and validates limits BEFORE opening a transaction.
    /// </summary>
    public class AddToLineRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly string _activeLineName;
        private readonly string _controllerName;
        private readonly double _maxLoadmA;
        private readonly int _maxAddressCount;
        private readonly HighlightRegistry _highlightRegistry;
        private readonly Action<AddToLineResult> _callback;

        /// <summary>Maximum number of detail messages to collect (avoids unbounded output).</summary>
        private const int MaxDetailMessages = 20;

        public AddToLineRequest(
            SettingsModel settings,
            string activeLineName,
            string controllerName,
            double maxLoadmA,
            int maxAddressCount,
            HighlightRegistry highlightRegistry,
            Action<AddToLineResult> callback)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _activeLineName = activeLineName;
            _controllerName = controllerName;
            _maxLoadmA = maxLoadmA;
            _maxAddressCount = maxAddressCount;
            _highlightRegistry = highlightRegistry;
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Execute(UIApplication app)
        {
            var result = new AddToLineResult();
            var log = App.Logger;

            try
            {
                log?.Info($"AddToLine: starting for line '{_activeLineName}'.");

                // --- Validate inputs ---
                if (string.IsNullOrWhiteSpace(_activeLineName))
                {
                    result.Message = "Cannot add to line: line name is empty.";
                    DispatchResult(result);
                    return;
                }

                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    result.Message = "No active document.";
                    DispatchResult(result);
                    return;
                }

                var doc = uidoc.Document;
                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();

                if (selectedIds == null || selectedIds.Count == 0)
                {
                    result.Message = "No elements selected.";
                    DispatchResult(result);
                    return;
                }

                // --- Build category filter set ---
                var includedCategoryIds = new HashSet<int>();
                foreach (var bic in _settings.IncludedCategories)
                {
                    includedCategoryIds.Add((int)bic);
                }

                // --- Phase 1: Filter elements and compute totals (read-only, no transaction) ---
                var validElements = new List<Element>();
                var typeCache = new Dictionary<long, CachedTypeData>();

                foreach (var eid in selectedIds)
                {
                    Element element;
                    try
                    {
                        element = doc.GetElement(eid);
                    }
                    catch
                    {
                        result.SkippedCount++;
                        AddDetail(result, $"Could not resolve element ID {eid}.");
                        continue;
                    }

                    if (element == null)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // Skip element types (only instances)
                    if (element is ElementType)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // Skip linked elements
                    if (element is RevitLinkInstance)
                    {
                        result.SkippedCount++;
                        AddDetail(result, $"Skipped linked element: {element.Name}");
                        continue;
                    }

                    // Check category
                    Category cat = element.Category;
                    if (cat == null)
                    {
                        result.SkippedCount++;
                        AddDetail(result, $"Skipped element without category: ID {eid}");
                        continue;
                    }

#if NET48
                    int catIdInt = cat.Id.IntegerValue;
#else
                    int catIdInt = (int)cat.Id.Value;
#endif

                    if (!includedCategoryIds.Contains(catIdInt))
                    {
                        result.SkippedCount++;
                        // Not in included categories -- silent skip
                        continue;
                    }

                    // Resolve FamilySymbol for type parameter reading
                    ElementId typeId = element.GetTypeId();
#if NET48
                    if (typeId == null || typeId == ElementId.InvalidElementId)
#else
                    if (typeId == ElementId.InvalidElementId)
#endif
                    {
                        result.SkippedCount++;
                        AddDetail(result, $"Element '{element.Name}' has no type -- cannot validate.");
                        continue;
                    }

#if NET48
                    long typeIdKey = (long)typeId.IntegerValue;
#else
                    long typeIdKey = typeId.Value;
#endif

                    // Read and cache type parameters (mA Load + Address Count)
                    if (!typeCache.TryGetValue(typeIdKey, out var cached))
                    {
                        cached = ReadTypeData(doc, typeId);
                        typeCache[typeIdKey] = cached;
                    }

                    // If type is missing required parameters, BLOCK this element
                    if (!cached.IsValid)
                    {
                        result.SkippedCount++;
                        if (cached.Warning != null)
                        {
                            AddDetail(result, cached.Warning);
                        }
                        continue;
                    }

                    // Accumulate totals from valid elements
                    result.TotalLoadmA += cached.LoadmA;
                    result.TotalAddressCount += cached.AddressCount;
                    validElements.Add(element);
                }

                // --- Phase 2: Validate limits ---
                if (validElements.Count == 0)
                {
                    result.Message = $"No valid elements to assign. Skipped: {result.SkippedCount}.";
                    log?.Warning($"AddToLine: blocked -- no valid elements. Skipped: {result.SkippedCount}.");
                    DispatchResult(result);
                    return;
                }

                log?.Info($"AddToLine: Phase 1 complete. Valid: {validElements.Count}, Skipped: {result.SkippedCount}, Load: {result.TotalLoadmA:N1} mA, Addr: {result.TotalAddressCount}.");

                if (result.TotalLoadmA > _maxLoadmA)
                {
                    result.Message = $"BLOCKED: Total load {result.TotalLoadmA:N1} mA exceeds limit of {_maxLoadmA:N0} mA.";
                    DispatchResult(result);
                    return;
                }

                if (result.TotalAddressCount > _maxAddressCount)
                {
                    result.Message = $"BLOCKED: Total address count {result.TotalAddressCount} exceeds limit of {_maxAddressCount}.";
                    DispatchResult(result);
                    return;
                }

                // --- Phase 3: Write DALI_Line_ID and DALI_Controller to instance elements ---
                string lineIdParamName = _settings.Param_LineId;
                string controllerParamName = _settings.Param_Controller;

                if (string.IsNullOrWhiteSpace(lineIdParamName))
                {
                    result.Message = "Instance parameter name (DALI_Line_ID) is not configured in Settings.";
                    DispatchResult(result);
                    return;
                }

                string trimmedLineName = _activeLineName.Trim();
                string trimmedControllerName = _controllerName?.Trim();

                using (var trans = new Transaction(doc, "DALI: Add to Line"))
                {
                    trans.Start();

                    foreach (var element in validElements)
                    {
                        try
                        {
                            // 1. Write Line ID
                            Parameter lineParam = element.LookupParameter(lineIdParamName);
                            if (lineParam == null)
                            {
                                result.FailedCount++;
                                AddDetail(result, $"Element '{element.Name}' (ID {element.Id}): missing instance parameter '{lineIdParamName}'.");
                                continue;
                            }

                            if (lineParam.IsReadOnly)
                            {
                                result.FailedCount++;
                                AddDetail(result, $"Element '{element.Name}' (ID {element.Id}): parameter '{lineIdParamName}' is read-only.");
                                continue;
                            }

                            // Check for previous value (for reassignment tracking)
                            string previousValue = lineParam.StorageType == StorageType.String
                                ? lineParam.AsString()
                                : lineParam.AsValueString();

                            if (!string.IsNullOrWhiteSpace(previousValue) && previousValue != trimmedLineName)
                            {
                                result.ReassignedCount++;
                            }

                            bool written = lineParam.Set(trimmedLineName);
                            if (!written)
                            {
                                result.FailedCount++;
                                AddDetail(result, $"Element '{element.Name}' (ID {element.Id}): Set() returned false for '{lineIdParamName}'.");
                                continue;
                            }

                            // 2. Write Controller (Optional)
                            if (!string.IsNullOrWhiteSpace(controllerParamName) && !string.IsNullOrWhiteSpace(trimmedControllerName))
                            {
                                Parameter ctrlParam = element.LookupParameter(controllerParamName);
                                if (ctrlParam != null && !ctrlParam.IsReadOnly)
                                {
                                    ctrlParam.Set(trimmedControllerName);
                                }
                                // We don't fail the entire operation if Controller param is missing, just skip it.
                                // But maybe we should log a warning?
                            }

                            result.UpdatedCount++;
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            AddDetail(result, $"Element '{element.Name}' (ID {element.Id}): {ex.Message}");
                        }
                    }

                    trans.Commit();
                }

                // Build summary message
                result.Success = true;
                result.Message = $"Assigned '{trimmedLineName}' to {result.UpdatedCount} element(s).";
                log?.Info($"AddToLine: wrote '{trimmedLineName}' to {result.UpdatedCount} element(s). Failed: {result.FailedCount}, Reassigned: {result.ReassignedCount}.");
                if (result.ReassignedCount > 0)
                    result.Message += $" ({result.ReassignedCount} reassigned from another line.)";
                if (result.FailedCount > 0)
                    result.Message += $" {result.FailedCount} failed.";
                if (result.SkippedCount > 0)
                    result.Message += $" {result.SkippedCount} skipped.";

                // --- Phase 4: Apply view filter highlight (non-blocking) ---
                // Runs in a separate transaction after the write commits.
                // Highlight failures do NOT roll back the parameter writes.
                try
                {
                    var view = doc.ActiveView;
                    if (view != null && _highlightRegistry != null)
                    {
                        var highlighter = new ViewFilterHighlighter();
                        using (var highlightTrans = new Transaction(doc, "DALI: Apply Highlight"))
                        {
                            highlightTrans.Start();
                            var hlResult = highlighter.ApplyLineHighlight(
                                doc, view, _settings, trimmedLineName, _highlightRegistry);
                            highlightTrans.Commit();

                            if (hlResult.Success)
                            {
                                result.Message += $" {hlResult.Message}";
                            }
                            else if (!string.IsNullOrEmpty(hlResult.Message))
                            {
                                AddDetail(result, $"Highlight: {hlResult.Message}");
                            }
                        }
                    }
                }
                catch (Exception hlEx)
                {
                    // Highlight failure is non-blocking
                    AddDetail(result, $"Highlight failed: {hlEx.Message}");
                    log?.Warning($"AddToLine: highlight failed: {hlEx.Message}");
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Error during Add to Line: {ex.Message}";
            }

            DispatchResult(result);
        }

        /// <summary>
        /// Reads type-level DALI parameters (mA Load, Address Count) for limit validation.
        /// Uses the same logic as SelectionTotalsService but inline to avoid allocation overhead.
        /// </summary>
        private CachedTypeData ReadTypeData(Document doc, ElementId typeId)
        {
            var data = new CachedTypeData();

            Element typeElement = doc.GetElement(typeId);
            if (typeElement == null || !(typeElement is FamilySymbol symbol))
            {
                data.Warning = $"Type ID {typeId} is not a FamilySymbol.";
                return data;
            }

            string typeName = $"{symbol.FamilyName} : {symbol.Name}";
            bool hasLoad = false;
            bool hasAddr = false;

            // Read mA Load
            if (!string.IsNullOrWhiteSpace(_settings.Param_Load))
            {
                Parameter loadParam = symbol.LookupParameter(_settings.Param_Load);
                if (loadParam != null)
                {
                    hasLoad = true;
                    data.LoadmA = SafeReadDouble(loadParam);
                }
                else
                {
                    data.Warning = $"Type '{typeName}': missing '{_settings.Param_Load}'.";
                }
            }

            // Read Address Count
            if (!string.IsNullOrWhiteSpace(_settings.Param_AddressCount))
            {
                Parameter addrParam = symbol.LookupParameter(_settings.Param_AddressCount);
                if (addrParam != null)
                {
                    hasAddr = true;
                    data.AddressCount = SafeReadInt(addrParam);
                }
                else
                {
                    string msg = $"Type '{typeName}': missing '{_settings.Param_AddressCount}'.";
                    data.Warning = data.Warning != null ? data.Warning + " | " + msg : msg;
                }
            }

            data.IsValid = hasLoad || hasAddr;
            return data;
        }

        private static double SafeReadDouble(Parameter param)
        {
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.Double: return param.AsDouble();
                    case StorageType.Integer: return (double)param.AsInteger();
                    case StorageType.String:
                        if (double.TryParse(param.AsString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double v))
                            return v;
                        return 0.0;
                    default: return 0.0;
                }
            }
            catch { return 0.0; }
        }

        private static int SafeReadInt(Parameter param)
        {
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.Integer: return param.AsInteger();
                    case StorageType.Double: return (int)param.AsDouble();
                    case StorageType.String:
                        if (int.TryParse(param.AsString(), out int v)) return v;
                        return 0;
                    default: return 0;
                }
            }
            catch { return 0; }
        }

        /// <summary>Adds a detail message if under the cap.</summary>
        private static void AddDetail(AddToLineResult result, string msg)
        {
            if (result.Details.Count < MaxDetailMessages)
            {
                result.Details.Add(msg);
            }
        }

        /// <summary>Marshals the result back to the WPF UI thread.</summary>
        private void DispatchResult(AddToLineResult result)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.Invoke(() => _callback(result));
            }
            else
            {
                _callback(result);
            }
        }

        /// <summary>Internal cache for type-level DALI parameter values.</summary>
        private class CachedTypeData
        {
            public bool IsValid { get; set; }
            public double LoadmA { get; set; }
            public int AddressCount { get; set; }
            public string Warning { get; set; }
        }
    }
}
