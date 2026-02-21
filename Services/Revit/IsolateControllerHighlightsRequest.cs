using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dali.Models;
using System;
using System.Collections.Generic;

namespace Dali.Services.Revit
{
    public class LineHighlightInfo
    {
        public string PanelName { get; set; }
        public string ControllerModelName { get; set; }
        public string LineName { get; set; }
        public string ControllerName { get; set; }
        public string ColorHex { get; set; }
    }

    public class IsolateControllerHighlightsRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly HighlightRegistry _registry;
        private readonly List<LineHighlightInfo> _activeLines;
        private readonly Action<string> _callback;

        public IsolateControllerHighlightsRequest(
            SettingsModel settings,
            HighlightRegistry registry,
            List<LineHighlightInfo> activeLines,
            Action<string> callback)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _activeLines = activeLines ?? new List<LineHighlightInfo>();
            _callback = callback;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    _callback?.Invoke("No active view to update highlights.");
                    return;
                }

                var doc = uidoc.Document;
                var view = doc.ActiveView;

                if (view == null)
                {
                    _callback?.Invoke("No active view.");
                    return;
                }

#if NET48
                long viewIdVal = (long)view.Id.IntegerValue;
#else
                long viewIdVal = view.Id.Value;
#endif

                var highlighter = new ViewFilterHighlighter();
                using (var trans = new Transaction(doc, "DALI: Isolate Controller Highlights"))
                {
                    trans.Start();

                    // 1. Reset all current overrides
                    var trackedFilters = _registry.GetFiltersForView(viewIdVal);
                    if (trackedFilters != null && trackedFilters.Count > 0)
                    {
                        highlighter.ResetHighlights(doc, view, trackedFilters, _registry);
                    }

                    // 2. Re-apply highlights for active controller lines
                    foreach (var line in _activeLines)
                    {
                        if (string.IsNullOrWhiteSpace(line.LineName)) continue;
                        
                        string pName = line.PanelName?.Trim() ?? string.Empty;
                        string cName = line.ControllerName?.Trim() ?? string.Empty;
                        string dName = line.ControllerModelName?.Trim() ?? string.Empty;
                        string combinedControllerString = $"{pName} - {cName} - {dName}".Trim(' ', '-');

                        highlighter.ApplyLineHighlight(
                            doc, 
                            view, 
                            _settings, 
                            combinedControllerString, 
                            line.LineName, 
                            _registry, 
                            line.ColorHex);
                    }

                    trans.Commit();
                }

                _callback?.Invoke($"Isolated view highlights to {_activeLines.Count} line(s).");
            }
            catch (Exception ex)
            {
                App.Logger?.Error("IsolateControllerHighlights exception", ex);
                _callback?.Invoke($"Error isolating highlights: {ex.Message}");
            }
        }
    }
}
