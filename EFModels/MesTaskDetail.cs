using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class MesTaskDetail
    {
        public string TaskNoFromMes { get; set; } = null!;
        public int TaskType { get; set; }
        public string TaskNoFromSwarmCore { get; set; } = null!;
        public string Amrid { get; set; } = null!;
        public string FromStation { get; set; } = null!;
        public string ToStation { get; set; } = null!;
        public string Barcode { get; set; } = null!;
        public bool LoaderToAmrhighOrLow { get; set; }
        public bool AmrtoLoaderHighOrLow { get; set; }
        public int Priority { get; set; }
        public int Status { get; set; }
        public string GetFromMesTime { get; set; } = null!;
        public string AssignToSwarmCoreTime { get; set; } = null!;
        public string SwarmCoreActualStratTime { get; set; } = null!;
        public string FailTime { get; set; } = null!;
        public string FinishOrTimeoutTime { get; set; } = null!;
        public string FinishReason { get; set; } = null!;
    }
}
