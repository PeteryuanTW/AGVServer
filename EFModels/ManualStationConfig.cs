using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class ManualStationConfig
    {
        public decimal No { get; set; }
        public string Name { get; set; } = null!;
        public bool CheckBarcode { get; set; }
    }
}
