using Autodesk.Revit.DB;
using Dali.Models;
using Dali.Services.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dali.Services
{
    public class ParameterResolver
    {
        private readonly ILogger _logger;

        public ParameterResolver(ILogger logger)
        {
            _logger = logger;
        }

        public ValidationResult ValidateSettings(Document doc, SettingsModel settings)
        {
            var result = new ValidationResult { IsValid = true };

            try
            {
                // Validate Type Parameters
                ValidateParameter(doc, settings.Param_Load, true, settings, result);
                ValidateParameter(doc, settings.Param_AddressCount, true, settings, result);

                // Validate Instance Parameters
                ValidateParameter(doc, settings.Param_LineId, false, settings, result);
                ValidateParameter(doc, settings.Param_Controller, false, settings, result);

                // Validate visualization grouping parameters (optional — skip if empty)
                if (!string.IsNullOrWhiteSpace(settings.DeviceGroupingParamFixtures))
                {
                    var fixtureCategories = new List<BuiltInCategory>
                    {
                        BuiltInCategory.OST_LightingFixtures,
                        BuiltInCategory.OST_ElectricalFixtures
                    };
                    ValidateParameterForCategories(doc, settings.DeviceGroupingParamFixtures,
                        false, fixtureCategories, "Lighting Fixtures / El. Fixtures", result);
                }

                if (!string.IsNullOrWhiteSpace(settings.DeviceGroupingParamDevices))
                {
                    var deviceCategories = new List<BuiltInCategory>
                    {
                        BuiltInCategory.OST_LightingDevices
                    };
                    ValidateParameterForCategories(doc, settings.DeviceGroupingParamDevices,
                        false, deviceCategories, "Lighting Devices", result);
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Validation failed with exception: {ex.Message}");
                _logger.Error("Validation error", ex);
            }

            return result;
        }

        private void ValidateParameter(Document doc, string paramName, bool isTypeParam, SettingsModel settings, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(paramName))
            {
                result.AddError($"Parameter name cannot be empty.");
                return;
            }

            bool found = false;

            // Check if parameter exists on any of the included categories
            foreach (var categoryEnum in settings.IncludedCategories)
            {
                var category = Category.GetCategory(doc, categoryEnum);
                if (category == null) continue;

                var collector = new FilteredElementCollector(doc).OfCategoryId(category.Id);
                
                if (isTypeParam)
                {
                    collector.WhereElementIsElementType();
                }
                else
                {
                    collector.WhereElementIsNotElementType();
                }

                var firstElement = collector.FirstElement();
                if (firstElement == null) continue;

                Parameter param = firstElement.LookupParameter(paramName);
                if (param != null)
                {
                    found = true;
                    // Optional: Check StorageType here if required strict type validation
                    break;
                }
            }

            if (found)
            {
                result.AddSuccess($"Parameter '{paramName}' found.");
            }
            else
            {
                result.AddError($"Parameter '{paramName}' not found on any included categories (IsType: {isTypeParam}).");
            }
        }

        /// <summary>
        /// Validates a parameter against a specific set of categories (not the full IncludedCategories list).
        /// Scans ALL elements of each category (not just the first) because shared parameters may only exist
        /// on some family types within the same category.
        /// Checks both instances and element types so that Family Type shared params are also found.
        /// </summary>
        private void ValidateParameterForCategories(Document doc, string paramName, bool isTypeParam,
            IEnumerable<BuiltInCategory> categories, string categoryLabel, ValidationResult result)
        {
            string paramNorm = paramName.Normalize(System.Text.NormalizationForm.FormC);
            bool found = false;

            foreach (var bic in categories)
            {
                if (found) break;
                var category = Category.GetCategory(doc, bic);
                if (category == null) continue;

                // Check both instances and element types — shared params can be on either.
                var passes = new[]
                {
                    new FilteredElementCollector(doc).OfCategoryId(category.Id).WhereElementIsNotElementType(),
                    new FilteredElementCollector(doc).OfCategoryId(category.Id).WhereElementIsElementType()
                };

                foreach (var collector in passes)
                {
                    if (found) break;
                    foreach (Element elem in collector)
                    {
                        // Exact name lookup first (fast path)
                        Parameter param = elem.LookupParameter(paramName);

                        // Fallback: case-insensitive + Unicode NFC scan
                        if (param == null)
                        {
                            foreach (Parameter p in elem.Parameters)
                            {
                                string defName = p.Definition?.Name;
                                if (defName == null) continue;
                                if (string.Equals(
                                        defName.Normalize(System.Text.NormalizationForm.FormC),
                                        paramNorm,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    param = p;
                                    break;
                                }
                            }
                        }

                        if (param != null)
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }

            if (found)
                result.AddSuccess($"Grouping parameter '{paramName}' found ({categoryLabel}).");
            else
                result.AddError($"Grouping parameter '{paramName}' not found on {categoryLabel}.");
        }
    }
}
