using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dali.Models;
using System;
using System.Collections.Generic;

namespace Dali.Services.Revit
{
    public class RenameElementsResult
    {
        public bool Success { get; set; } = false;
        public int UpdatedCount { get; set; } = 0;
        public string Message { get; set; } = string.Empty;
        public List<string> Details { get; set; } = new List<string>();
    }

    public enum RenameScope
    {
        Controller,
        Line
    }

    public class RenameElementsRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly RenameScope _scope;
        private readonly string _oldName;
        private readonly string _newName;
        private readonly string _parentName;
        private readonly Action<RenameElementsResult> _callback;

        public RenameElementsRequest(
            SettingsModel settings,
            RenameScope scope,
            string oldName,
            string newName,
            string parentName,
            Action<RenameElementsResult> callback)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _scope = scope;
            _oldName = oldName?.Trim() ?? string.Empty;
            _newName = newName?.Trim() ?? string.Empty;
            _parentName = parentName?.Trim() ?? string.Empty;
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Execute(UIApplication app)
        {
            var result = new RenameElementsResult();
            var log = App.Logger;

            try
            {
                if (string.IsNullOrWhiteSpace(_oldName) || string.IsNullOrWhiteSpace(_newName))
                {
                    result.Message = "Rename aborted: missing old or new name.";
                    _callback(result);
                    return;
                }

                if (_oldName == _newName)
                {
                    result.Success = true;
                    result.Message = "No change necessary.";
                    _callback(result);
                    return;
                }

                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    result.Message = "No active document.";
                    _callback(result);
                    return;
                }

                var doc = uidoc.Document;

                var includedCategoryIds = new HashSet<int>();
                foreach (var bic in _settings.IncludedCategories)
                    includedCategoryIds.Add((int)bic);

                string lineIdParamName = _settings.Param_LineId;
                string controllerParamName = _settings.Param_Controller;

                if (string.IsNullOrWhiteSpace(lineIdParamName) || string.IsNullOrWhiteSpace(controllerParamName))
                {
                    result.Message = "Parameter mappings not fully configured in settings.";
                    _callback(result);
                    return;
                }

                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                var elementsToRename = new List<Element>();

                foreach (Element element in collector)
                {
                    if (element is ElementType || element is RevitLinkInstance) continue;
                    Category cat = element.Category;
                    if (cat == null) continue;

#if NET48
                    int catIdInt = cat.Id.IntegerValue;
#else
                    int catIdInt = (int)cat.Id.Value;
#endif
                    if (!includedCategoryIds.Contains(catIdInt)) continue;

                    Parameter lineParam = element.LookupParameter(lineIdParamName);
                    Parameter ctrlParam = element.LookupParameter(controllerParamName);

                    string lineVal = lineParam?.StorageType == StorageType.String ? lineParam.AsString()?.Trim() : lineParam?.AsValueString()?.Trim();
                    string ctrlVal = ctrlParam?.StorageType == StorageType.String ? ctrlParam.AsString()?.Trim() : ctrlParam?.AsValueString()?.Trim();

                    if (_scope == RenameScope.Line)
                    {
                        if (lineVal == _oldName && (string.IsNullOrWhiteSpace(_parentName) || ctrlVal == _parentName))
                        {
                            elementsToRename.Add(element);
                        }
                    }
                    else if (_scope == RenameScope.Controller)
                    {
                        if (ctrlVal == _oldName)
                        {
                            elementsToRename.Add(element);
                        }
                    }
                }

                if (elementsToRename.Count == 0)
                {
                    result.Success = true;
                    result.Message = $"Renamed {_scope}: 0 elements found assigned to '{_oldName}'.";
                    _callback(result);
                    return;
                }

                using (var trans = new Transaction(doc, $"DALI: Rename {_scope}"))
                {
                    trans.Start();
                    
                    foreach (var element in elementsToRename)
                    {
                        try
                        {
                            if (_scope == RenameScope.Line)
                            {
                                Parameter lineParam = element.LookupParameter(lineIdParamName);
                                if (lineParam != null && !lineParam.IsReadOnly)
                                {
                                    lineParam.Set(_newName);
                                    result.UpdatedCount++;
                                }
                            }
                            else if (_scope == RenameScope.Controller)
                            {
                                Parameter ctrlParam = element.LookupParameter(controllerParamName);
                                if (ctrlParam != null && !ctrlParam.IsReadOnly)
                                {
                                    ctrlParam.Set(_newName);
                                    result.UpdatedCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Details.Add($"Failed ID {element.Id}: {ex.Message}");
                        }
                    }

                    if (result.UpdatedCount > 0)
                    {
                        trans.Commit();
                        result.Success = true;
                        result.Message = $"Renamed {_scope}: synchronized {result.UpdatedCount} element(s) to '{_newName}'.";
                        log?.Info(result.Message);
                    }
                    else
                    {
                        trans.RollBack();
                        result.Success = false;
                        result.Message = $"Failed to rename any of the {elementsToRename.Count} matched elements.";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error parsing rename: {ex.Message}";
                log?.Error($"RenameElementsRequest Exception: {ex}", ex);
            }

            _callback(result);
        }
    }
}
