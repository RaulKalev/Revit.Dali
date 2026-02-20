using System.Collections.Generic;

namespace Dali.Models
{
    public class PanelDefinition
    {
        public string Name { get; set; } = "New Panel";
        public List<ControllerDefinition> Controllers { get; set; } = new List<ControllerDefinition>();
    }
}
