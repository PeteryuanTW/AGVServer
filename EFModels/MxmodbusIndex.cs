using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class MxmodbusIndex
    {
        public string Plctype { get; set; } = null!;
        public string VariableType { get; set; } = null!;
        public decimal MxIndex { get; set; }
        public decimal Offset { get; set; }
    }
}
