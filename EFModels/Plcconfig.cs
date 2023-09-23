using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class Plcconfig
    {
        public decimal No { get; set; }
        public string Ip { get; set; } = null!;
        public decimal Port { get; set; }
        public string Name { get; set; } = null!;
        public string Type { get; set; } = null!;
        public string Plctype { get; set; } = null!;
        public decimal ModbusStartAddress { get; set; }
        public bool CheckBarcode { get; set; }
        public bool Enabled { get; set; }
    }
}
