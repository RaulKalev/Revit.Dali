using System.Collections.Generic;

namespace Dali.Models
{
    public class ControllerDefinition
    {
        public string Name { get; set; } = "New Controller";
        public string DeviceId { get; set; }
        public List<LineDefinition> Lines { get; set; } = new List<LineDefinition>();
    }
}
