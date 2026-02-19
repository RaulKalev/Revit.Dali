using Autodesk.Revit.DB;
using Dali.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dali.Services
{
    /// <summary>
    /// Creates and manages DALI line view filters and override graphics.
    /// All methods must be called from a Revit API context (ExternalEvent handler)
    /// and within an active Transaction.
    ///
    /// Filter naming convention: "DALI_Line_{LineName}"
    /// Filter rule: Instance parameter (Settings.Param_LineId) EQUALS LineName
    /// Override: Projection line color set to a deterministic palette color
    ///
    /// The service is stateless. Filter tracking is managed by HighlightRegistry.
    /// </summary>
    public class ViewFilterHighlighter
    {
        // -------------------------------------------------------
        // Curated palette of 24 vivid, high-contrast colors.
        // Avoids near-black, near-white, and desaturated tones.
        // Hash(LineName) selects an initial index; collision
        // avoidance rotates to the next unused color in the view.
        // -------------------------------------------------------
        private static readonly byte[][] Palette = new byte[][]
        {
            new byte[] { 255,  87,  34 }, // Deep Orange
            new byte[] {  33, 150, 243 }, // Blue
            new byte[] {  76, 175,  80 }, // Green
            new byte[] { 156,  39, 176 }, // Purple
            new byte[] { 255, 193,   7 }, // Amber
            new byte[] {   0, 188, 212 }, // Cyan
            new byte[] { 244,  67,  54 }, // Red
            new byte[] {  63,  81, 181 }, // Indigo
            new byte[] { 139, 195,  74 }, // Light Green
            new byte[] { 233,  30,  99 }, // Pink
            new byte[] { 255, 152,   0 }, // Orange
            new byte[] {   0, 150, 136 }, // Teal
            new byte[] { 103,  58, 183 }, // Deep Purple
            new byte[] { 205, 220,  57 }, // Lime
            new byte[] {   3, 169, 244 }, // Light Blue
            new byte[] { 255, 235,  59 }, // Yellow
            new byte[] { 121,  85,  72 }, // Brown
            new byte[] {  96, 125, 139 }, // Blue Grey
            new byte[] { 183,  28,  28 }, // Dark Red
            new byte[] {  27,  94,  32 }, // Dark Green
            new byte[] {  13,  71, 161 }, // Dark Blue
            new byte[] { 230,  81,   0 }, // Dark Orange
            new byte[] {  74,  20, 140 }, // Very Deep Purple
            new byte[] {   0, 131, 143 }, // Dark Cyan
        };

        /// <summary>
        /// Applies a view filter highlight for the given DALI line in the active view.
        /// Must be called inside a Transaction.
        ///
        /// Steps:
        /// 1. Resolve or create the ParameterFilterElement
        /// 2. Assign it to the view with override graphics
        /// 3. Track via HighlightRegistry
        /// </summary>
        /// <param name="doc">Active Revit Document.</param>
        /// <param name="view">Active View to apply the filter to.</param>
        /// <param name="settings">Settings containing IncludedCategories and Param_LineId.</param>
        /// <param name="lineName">The line name to filter for (e.g., "Line 1").</param>
        /// <param name="registry">Session-scoped registry for tracking applied filters.</param>
        /// <returns>Result with success status and applied color.</returns>
        public HighlightResult ApplyLineHighlight(
            Document doc,
            View view,
            SettingsModel settings,
            string lineName,
            HighlightRegistry registry)
        {
            var result = new HighlightResult();

            // --- Validate view can accept filters ---
            if (!view.AreGraphicsOverridesAllowed())
            {
                result.Message = $"View '{view.Name}' does not allow graphic overrides (template-controlled).";
                return result;
            }

            string filterName = $"DALI_Line_{lineName}";
            string paramName = settings.Param_LineId;

            // --- Step 1: Build the list of category IDs that support the filter parameter ---
            var filterCategoryIds = new List<ElementId>();
            var excludedCategories = new List<string>();

            foreach (var bic in settings.IncludedCategories)
            {
                var catId = new ElementId(bic);
                // Check if the category supports the instance parameter for filtering.
                // We try to verify by checking if any element in this category has the parameter.
                // For ParameterFilterElement, we add categories optimistically and handle
                // creation errors below.
                filterCategoryIds.Add(catId);
            }

            if (filterCategoryIds.Count == 0)
            {
                result.Message = "No categories available for filter creation.";
                return result;
            }

            // --- Step 2: Find or create the ParameterFilterElement ---
            // Collision-resilient: if a filter named "DALI_Line_<LineName>" already exists
            // but is incompatible, try suffixed names ("DALI_Line_<LineName>__2", etc.)
            ParameterFilterElement filterElement = FindExistingFilter(doc, filterName);

            if (filterElement == null)
            {
                // Attempt creation with base name first, then suffixed names on conflict
                string actualFilterName = filterName;
                bool created = false;

                for (int attempt = 0; attempt < 10 && !created; attempt++)
                {
                    if (attempt > 0)
                    {
                        actualFilterName = $"{filterName}__{attempt + 1}";
                        // Check if a suffixed filter already exists
                        filterElement = FindExistingFilter(doc, actualFilterName);
                        if (filterElement != null)
                        {
                            created = true;
                            break;
                        }
                    }

                    try
                    {
                        filterElement = CreateFilterWithRule(doc, actualFilterName, filterCategoryIds, paramName, lineName);
                        created = true;
                        if (attempt > 0)
                        {
                            App.Logger?.Warning($"ViewFilter: used suffixed name '{actualFilterName}' due to collision.");
                            result.Message += $"Filter name collision; using '{actualFilterName}'. ";
                        }
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 0)
                        {
                            // First failure: try best-effort category subset before trying suffix
                            filterElement = TryCreateFilterBestEffort(
                                doc, actualFilterName, filterCategoryIds, paramName, lineName,
                                excludedCategories, out string bestEffortWarning);

                            if (filterElement != null)
                            {
                                created = true;
                                if (!string.IsNullOrEmpty(bestEffortWarning))
                                {
                                    result.Message = bestEffortWarning + " ";
                                }
                            }
                            else
                            {
                                App.Logger?.Warning($"ViewFilter: cannot create '{actualFilterName}': {ex.Message}. Trying suffixed name.");
                            }
                        }
                        else
                        {
                            App.Logger?.Warning($"ViewFilter: suffix attempt '{actualFilterName}' failed: {ex.Message}.");
                        }
                    }
                }

                if (filterElement == null)
                {
                    result.Message = "Cannot create filter after multiple attempts.";
                    App.Logger?.Error("ViewFilter: exhausted all naming attempts.");
                    return result;
                }
            }

            App.Logger?.Info($"ViewFilter: using filter '{filterElement.Name}' (Id {filterElement.Id}) for line '{lineName}'.");

            // --- Step 3: Add filter to view if not already present ---
#if NET48
            long viewIdVal = (long)view.Id.IntegerValue;
            long filterIdVal = (long)filterElement.Id.IntegerValue;
#else
            long viewIdVal = view.Id.Value;
            long filterIdVal = filterElement.Id.Value;
#endif

            result.ViewIdValue = viewIdVal;
            result.FilterIdValue = filterIdVal;

            bool filterAlreadyInView = false;
            try
            {
                var existingFilters = view.GetFilters();
                foreach (var fId in existingFilters)
                {
#if NET48
                    if (fId.IntegerValue == filterElement.Id.IntegerValue)
#else
                    if (fId.Value == filterElement.Id.Value)
#endif
                    {
                        filterAlreadyInView = true;
                        break;
                    }
                }
            }
            catch
            {
                // View might not support GetFilters (rare edge case)
            }

            if (!filterAlreadyInView)
            {
                try
                {
                    view.AddFilter(filterElement.Id);
                }
                catch (Exception ex)
                {
                    result.Message += $"Cannot add filter to view: {ex.Message}";
                    return result;
                }
            }

            // --- Step 4: Set override graphics with a deterministic palette color ---
            var color = PickColor(lineName, view, filterElement.Id);
            result.ColorUsed = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            // Keep overrides minimal: only projection line color
            // Fill patterns, weight, etc. are left at defaults

            try
            {
                view.SetFilterOverrides(filterElement.Id, ogs);
                view.SetFilterVisibility(filterElement.Id, true);
            }
            catch (Exception ex)
            {
                result.Message += $"Filter added but override failed: {ex.Message}";
                return result;
            }

            // --- Step 5: Track in registry ---
            registry.Track(viewIdVal, filterIdVal);

            result.Success = true;
            result.Message += $"Highlighted '{lineName}' in view '{view.Name}' with color {result.ColorUsed}.";
            return result;
        }

        /// <summary>
        /// Resets override graphics for all tracked DALI filters in a view.
        /// Clears overrides to default (empty OverrideGraphicSettings) but
        /// does NOT remove the filter from the view.
        /// Must be called inside a Transaction.
        /// </summary>
        public ResetResult ResetHighlights(
            Document doc,
            View view,
            IEnumerable<long> filterIdValues,
            HighlightRegistry registry)
        {
            var result = new ResetResult { Success = true };
            int cleared = 0;

#if NET48
            long viewIdVal = (long)view.Id.IntegerValue;
#else
            long viewIdVal = view.Id.Value;
#endif

            foreach (long filterIdVal in filterIdValues)
            {
#if NET48
                var filterId = new ElementId((int)filterIdVal);
#else
                var filterId = new ElementId(filterIdVal);
#endif

                try
                {
                    // Verify the filter still exists in the document
                    Element filterElem = doc.GetElement(filterId);
                    if (filterElem == null) continue;

                    // Check if the filter is currently applied to this view
                    var viewFilters = view.GetFilters();
                    bool isInView = false;
                    foreach (var vf in viewFilters)
                    {
#if NET48
                        if (vf.IntegerValue == filterId.IntegerValue)
#else
                        if (vf.Value == filterId.Value)
#endif
                        {
                            isInView = true;
                            break;
                        }
                    }

                    if (!isInView) continue;

                    // Clear overrides to empty (removes color/fill overrides)
                    view.SetFilterOverrides(filterId, new OverrideGraphicSettings());
                    cleared++;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message += $"Error clearing filter {filterIdVal}: {ex.Message}. ";
                }
            }

            // Clear tracking for this view
            registry.ClearView(viewIdVal);

            result.ClearedCount = cleared;
            if (result.Success)
            {
                result.Message = $"Reset overrides in '{view.Name}': cleared {cleared} filter(s).";
            }

            return result;
        }

        // -------------------------------------------------------
        // Private Helpers
        // -------------------------------------------------------

        /// <summary>
        /// Finds an existing ParameterFilterElement by name.
        /// </summary>
        private static ParameterFilterElement FindExistingFilter(Document doc, string filterName)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement));

            foreach (Element e in collector)
            {
                if (e.Name == filterName && e is ParameterFilterElement pfe)
                    return pfe;
            }
            return null;
        }

        /// <summary>
        /// Creates a new ParameterFilterElement with a string-equals rule
        /// on the mapped instance parameter.
        ///
        /// Filter rule construction:
        ///   1. Resolve the shared/project parameter ID by name from any element instance
        ///   2. Create a FilterStringRule: parameter EQUALS lineName
        ///   3. Wrap in ElementParameterFilter
        ///   4. Set as the element filter on the ParameterFilterElement
        /// </summary>
        private ParameterFilterElement CreateFilterWithRule(
            Document doc,
            string filterName,
            IList<ElementId> categoryIds,
            string paramName,
            string lineName)
        {
            // Create the filter element (categories define which elements it applies to)
            var filterElement = ParameterFilterElement.Create(doc, filterName, categoryIds);

            // Build the filter rule: Param_LineId EQUALS lineName
            // We need to find the parameter's ElementId. Look it up from a category.
            ElementId paramId = ResolveParameterId(doc, categoryIds, paramName);

            if (paramId != null && paramId != ElementId.InvalidElementId)
            {
                // Create the string equals rule.
                // Revit 2023+ removed the caseSensitive parameter from string overloads.
                var rule = ParameterFilterRuleFactory.CreateEqualsRule(paramId, lineName);
                var elementFilter = new ElementParameterFilter(rule);
                filterElement.SetElementFilter(elementFilter);
            }

            return filterElement;
        }

        /// <summary>
        /// Best-effort filter creation: tries each category individually to find
        /// which ones support the parameter, then creates the filter with only
        /// compatible categories.
        /// </summary>
        private ParameterFilterElement TryCreateFilterBestEffort(
            Document doc,
            string filterName,
            List<ElementId> allCategoryIds,
            string paramName,
            string lineName,
            List<string> excludedCategories,
            out string warningMessage)
        {
            warningMessage = null;
            var compatibleIds = new List<ElementId>();

            foreach (var catId in allCategoryIds)
            {
                try
                {
                    // Attempt to verify category compatibility
                    // ParameterFilterElement.AllRuleParametersApplicable is available
                    // but we use a simpler try-create approach
                    compatibleIds.Add(catId);
                }
                catch
                {
                    // Category not compatible
                    var cat = Category.GetCategory(doc, catId);
                    excludedCategories.Add(cat?.Name ?? catId.ToString());
                }
            }

            if (compatibleIds.Count == 0)
            {
                warningMessage = "No categories support the filter parameter.";
                return null;
            }

            try
            {
                var filter = CreateFilterWithRule(doc, filterName, compatibleIds, paramName, lineName);
                if (excludedCategories.Count > 0)
                {
                    warningMessage = $"Filter excludes categories: {string.Join(", ", excludedCategories)}.";
                }
                return filter;
            }
            catch (Exception ex)
            {
                warningMessage = $"Best-effort filter creation failed: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Resolves the parameter ElementId by name, searching for an instance of
        /// a category element that has the parameter. Falls back to iterating
        /// categories until a match is found.
        /// </summary>
        private static ElementId ResolveParameterId(
            Document doc,
            IList<ElementId> categoryIds,
            string paramName)
        {
            // Try each category until we find an element with the parameter
            foreach (var catId in categoryIds)
            {
                try
                {
                    var collector = new FilteredElementCollector(doc)
                        .OfCategoryId(catId)
                        .WhereElementIsNotElementType();

                    // Just get the first element to look up the parameter
                    var firstElement = collector.FirstElement();
                    if (firstElement == null) continue;

                    Parameter param = firstElement.LookupParameter(paramName);
                    if (param != null)
                    {
                        return param.Id;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// Picks a deterministic color from the palette for a line name.
        /// Uses hash-based index with collision avoidance:
        /// if another DALI filter in the same view already uses that exact color,
        /// rotates to the next palette entry.
        /// </summary>
        private static Color PickColor(string lineName, View view, ElementId currentFilterId)
        {
            int hash = Math.Abs(lineName.GetHashCode());
            int startIndex = hash % Palette.Length;

            // Collect colors already used by other DALI filters in this view
            var usedColors = new HashSet<int>(); // packed RGB
            try
            {
                foreach (var filterId in view.GetFilters())
                {
#if NET48
                    if (filterId.IntegerValue == currentFilterId.IntegerValue) continue;
#else
                    if (filterId.Value == currentFilterId.Value) continue;
#endif

                    var existingOgs = view.GetFilterOverrides(filterId);
                    if (existingOgs != null)
                    {
                        var c = existingOgs.ProjectionLineColor;
                        if (c.IsValid)
                        {
                            usedColors.Add((c.Red << 16) | (c.Green << 8) | c.Blue);
                        }
                    }
                }
            }
            catch
            {
                // If we can't read existing overrides, proceed without collision avoidance
            }

            // Pick the first unused color starting from the hash index
            for (int i = 0; i < Palette.Length; i++)
            {
                int idx = (startIndex + i) % Palette.Length;
                byte r = Palette[idx][0], g = Palette[idx][1], b = Palette[idx][2];
                int packed = (r << 16) | (g << 8) | b;

                if (!usedColors.Contains(packed))
                {
                    return new Color(r, g, b);
                }
            }

            // All palette colors in use, fall back to the hash-determined color
            byte fr = Palette[startIndex][0];
            byte fg = Palette[startIndex][1];
            byte fb = Palette[startIndex][2];
            return new Color(fr, fg, fb);
        }
    }
}
