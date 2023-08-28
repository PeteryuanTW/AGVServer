using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class CellConfig
    {
        public string CellName { get; set; } = null!;
        public bool AlignSide { get; set; }
        public string LeaveCell { get; set; } = null!;
    }
}
