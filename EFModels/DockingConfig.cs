using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class DockingConfig
    {
        public string Name { get; set; } = null!;
        public bool TopOrBut { get; set; }
        public string GoalName { get; set; } = null!;
    }
}
