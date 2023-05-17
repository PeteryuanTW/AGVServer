using System;
using System.Collections.Generic;

namespace AGVServer.EFModels
{
    public partial class Configuration
    {
        public string ConfigName { get; set; } = null!;
        public string ConfigValue { get; set; } = null!;
    }
}
