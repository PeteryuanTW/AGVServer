using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AGVServer.EFModels
{
    public partial class ImesTask
    {
        public string TaskNoFromMes { get; set; } = null!;
        public int TaskType { get; set; }
        public string FromStation { get; set; } = null!;
        public string ToStation { get; set; } = null!;
        public string Barcode { get; set; } = null!;
        public bool LoaderToAmrhighOrLow { get; set; }
        public bool AmrtoLoaderHighOrLow { get; set; }
        public int Priority { get; set; }
        public int DelaySecond { get; set; }
        public string GetFromMesTime { get; set; } = null!;
    }
}
