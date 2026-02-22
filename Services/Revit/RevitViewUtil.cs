using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Dali.Services.Revit
{
    /// <summary>
    /// Small utility helpers for forcing Revit view repaints.
    /// </summary>
    internal static class RevitViewUtil
    {
        /// <summary>
        /// Forces a full graphical repaint of the active view by committing two
        /// back-to-back transactions: one that changes the DisplayStyle away from
        /// its current value, and one that restores it.  This is the programmatic
        /// equivalent of the user toggling the canvas theme twice.
        ///
        /// Must be called on the Revit API thread (inside an IExternalEventRequest.Execute).
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

                // Transaction 1: change to temp style → committed change triggers redraw queue.
                using (var t1 = new Transaction(doc, "DALI: Repaint Step 1"))
                {
                    t1.Start();
                    view.DisplayStyle = temp;
                    t1.Commit();
                }

                // Transaction 2: restore original style → second committed change completes the cycle.
                using (var t2 = new Transaction(doc, "DALI: Repaint Step 2"))
                {
                    t2.Start();
                    view.DisplayStyle = original;
                    t2.Commit();
                }

                uidoc?.RefreshActiveView();
            }
            catch
            {
                // Best-effort: some view types (schedules, sheets) don't support DisplayStyle.
                // RefreshActiveView alone as fallback.
                try { uidoc?.RefreshActiveView(); } catch { }
            }
        }
    }
}
