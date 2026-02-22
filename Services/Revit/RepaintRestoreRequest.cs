using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Dali.Services.Revit
{
    /// <summary>
    /// Restores the DisplayStyle of a view to its original value.
    /// Fired as a second ExternalEvent after ForceRepaint's first transaction,
    /// so Revit has had a full render cycle between the two style changes.
    /// </summary>
    internal class RepaintRestoreRequest : IExternalEventRequest
    {
        private readonly ElementId _viewId;
        private readonly DisplayStyle _originalStyle;

        public RepaintRestoreRequest(ElementId viewId, DisplayStyle originalStyle)
        {
            _viewId = viewId;
            _originalStyle = originalStyle;
        }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            var view = doc.GetElement(_viewId) as View;
            if (view == null) return;

            try
            {
                using (var t = new Transaction(doc, "DALI: Repaint Step 2"))
                {
                    t.Start();
                    view.DisplayStyle = _originalStyle;
                    t.Commit();
                }
                uidoc.RefreshActiveView();
            }
            catch { /* best-effort */ }
        }
    }
}
