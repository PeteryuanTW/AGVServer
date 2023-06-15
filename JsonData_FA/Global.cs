using Newtonsoft.Json;

namespace AGVServer.JsonData_FA
{
    public class Global
    {
        [JsonProperty("global-attribute-key")]
        public string globalattributekey { get; set; }
        public string assigned_robot { get; set; }
    }
}
