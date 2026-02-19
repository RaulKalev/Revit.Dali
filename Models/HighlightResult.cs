namespace Dali.Models
{
    /// <summary>
    /// Result of applying a view filter highlight to the active view.
    /// </summary>
    public class HighlightResult
    {
        /// <summary>True if the filter was applied or already exists with overrides set.</summary>
        public bool Success { get; set; }

        /// <summary>Human-readable status message.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>The ElementId value of the view where highlight was applied (for registry tracking).</summary>
        public long ViewIdValue { get; set; }

        /// <summary>The ElementId value of the ParameterFilterElement used.</summary>
        public long FilterIdValue { get; set; }

        /// <summary>The RGB color applied as projection line override, formatted "#RRGGBB".</summary>
        public string ColorUsed { get; set; } = string.Empty;
    }
}
