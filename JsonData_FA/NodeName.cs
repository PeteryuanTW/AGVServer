using Newtonsoft.Json;
namespace AGVServer.JsonData_FA
{
	public class NodeName
	{
		[JsonProperty("local-attribute-key")]
		public string localattributekey { get; set; }
		public string assigned_robot { get; set; }
	}
}
