using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

// App lives in the root Dali namespace
using Dali;

namespace Dali.Services.Revit
{
    /// <summary>
    /// Small utility helpers for forcing Revit view repaints.
    /// </summary>
    internal static class RevitViewUtil
    {
        /// <summary>
        /// Forces a full graphical repaint of the active view.
        ///
        /// Strategy: commit TX1 (DisplayStyle → temp) inside the current Execute() call,
        /// then let Execute() return so Revit can render the intermediate state.
        /// A scheduled ExternalEvent fires 200 ms later to commit TX2 (restore original)
        /// giving Revit a second render pass — this time with the filter overrides visible.
        ///
        /// Must be called on the Revit API thread (inside IExternalEventRequest.Execute).
        /// </summary>
        public static void ForceRepaint(Document doc, UIDocument uidoc, View view)
        {
            if (doc == null || view == null) return;

            try
            {
                var original = view.DisplayStyle;
                var temp = original == DisplayStyle.Wireframe
                    ? DisplayStyle.ShadingWithEdges
                    : DisplayStyle.Wireframe;

                // TX1: change to temp — fires inside the current Execute() call.
                using (var t1 = new Transaction(doc, "DALI: Repaint Step 1"))
                {
                    t1.Start();
                    view.DisplayStyle = temp;
                    t1.Commit();
                }
                uidoc?.RefreshActiveView();

                // After Execute() returns Revit will render the temp style.
                // Schedule TX2 as a brand-new ExternalEvent so Revit has rendered
                // before we restore — second render pass shows the filter colours.
                var viewId = view.Id;
                Task.Delay(200).ContinueWith(_ =>
                    App.ExternalEventService?.Raise(
                        new RepaintRestoreRequest(viewId, original)));
            }
            catch
            {
                try { uidoc?.RefreshActiveView(); } catch { }
            }
        }
    }
}
