using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class GroupConfig
    {
        public string GroupName { get; set; } = null!;
        public string Elements { get; set; } = null!;
        public bool Occupied { get; set; }
    }
}
