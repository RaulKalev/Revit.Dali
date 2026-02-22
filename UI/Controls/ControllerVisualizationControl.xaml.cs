using System.Windows.Controls;

namespace Dali.UI.Controls
{
    /// <summary>
    /// Code-behind for ControllerVisualizationControl.
    /// The schematic layout is handled entirely in XAML (vertical stem lines are inline
    /// Rectangle elements; no Canvas connector drawing needed).
    /// </summary>
    public partial class ControllerVisualizationControl : UserControl
    {
        public ControllerVisualizationControl()
        {
            InitializeComponent();
        }
    }

}
