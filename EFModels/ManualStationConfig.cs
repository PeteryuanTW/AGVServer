using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class ManualStationConfig
    {
        public decimal No { get; set; }
        public string Name { get; set; } = null!;
        public string RotateCell { get; set; } = null!;
        public string RotateDegree { get; set; } = null!;
        public string RotateDest { get; set; } = null!;
        public string GateInCell { get; set; } = null!;
        public string GateOutCell { get; set; } = null!;
        public string ArtifactId { get; set; } = null!;
    }
}
