using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class CellConfig
    {
        public string CellName { get; set; } = null!;
        public bool AlignSide { get; set; }
        public string GateInCell { get; set; } = null!;
        public string ArtifactId { get; set; } = null!;
        public string RotateCell { get; set; } = null!;
        public string RotateDegree { get; set; } = null!;
        public string RotateDest { get; set; } = null!;
        public string GateOutCell { get; set; } = null!;
    }
}
