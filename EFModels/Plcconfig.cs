﻿using System;
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
        public bool AlignSide { get; set; }
        public string RotateCell { get; set; } = null!;
        public string RotateDegree { get; set; } = null!;
        public string RotateDest { get; set; } = null!;
        public string GateInCell { get; set; } = null!;
        public string GateOutCell { get; set; } = null!;
        public string ArtifactId { get; set; } = null!;
        public string Plctype { get; set; } = null!;
        public decimal ModbusStartAddress { get; set; }
        public bool Enabled { get; set; }
    }
}
