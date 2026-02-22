using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dali.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dali.Services.Revit
{
    /// <summary>
    /// Highlights all elements on a specific DALI line in the active view and zooms to them.
    /// </summary>
    public class HighlightLineRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly HighlightRegistry _registry;
        private readonly string _panelName;
        private readonly string _controllerName;
        private readonly string _controllerModelName;
        private readonly string _lineName;
        private readonly string _colorHex;
        private readonly Action<string> _callback;

        public HighlightLineRequest(
            SettingsModel settings,
            HighlightRegistry registry,
            string panelName,
            string controllerName,
            string controllerModelName,
            string lineName,
            string colorHex,
            Action<string> callback)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _panelName = panelName ?? string.Empty;
            _controllerName = controllerName ?? string.Empty;
            _controllerModelName = controllerModelName ?? string.Empty;
            _lineName = lineName;
            _colorHex = colorHex;
            _callback = callback;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    _callback?.Invoke("No active document.");
                    return;
                }

                var doc = uidoc.Document;
                var view = doc.ActiveView;

                if (view == null)
                {
                    _callback?.Invoke("No active view.");
                    return;
                }

                string pName = _panelName?.Trim() ?? string.Empty;
                string cName = _controllerName?.Trim() ?? string.Empty;
                string dName = _controllerModelName?.Trim() ?? string.Empty;
                string combinedControllerString = $"{pName} - {cName} - {dName}".Trim(' ', '-');

                HighlightResult highlightResult;
                var highlighter = new ViewFilterHighlighter();
                using (var trans = new Transaction(doc, "DALI: Highlight Line"))
                {
                    trans.Start();
                    highlightResult = highlighter.ApplyLineHighlight(
                        doc,
                        view,
                        _settings,
                        combinedControllerString,
                        _lineName,
                        _registry,
                        _colorHex);

                    trans.Commit();
                }

                RevitViewUtil.ForceRepaint(doc, uidoc, view);

                if (!highlightResult.Success || highlightResult.ElementsOnLine == null || !highlightResult.ElementsOnLine.Any())
                {
                    _callback?.Invoke(highlightResult.Success
                        ? $"No elements found on '{_lineName}'."
                        : $"Highlight failed: {highlightResult.Message}");
                    return;
                }

                // Zoom the view to the highlighted elements
                try
                {
                    uidoc.ShowElements(new List<ElementId>(highlightResult.ElementsOnLine));
                }
                catch (Exception zoomEx)
                {
                    // Zoom is best-effort; highlight already applied
                    App.Logger?.Warning($"HighlightLine: zoom failed: {zoomEx.Message}");
                }

                _callback?.Invoke($"Highlighted and zoomed to {highlightResult.ElementsOnLine.Count} element(s) on '{_lineName}'.");
            }
            catch (Exception ex)
            {
                App.Logger?.Error("HighlightLine exception", ex);
                _callback?.Invoke($"Error highlighting line: {ex.Message}");
            }
        }
    }
}
