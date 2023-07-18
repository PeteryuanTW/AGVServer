using Newtonsoft.Json;

namespace AGVServer.JsonData_FA

{
	public class Args
	{
		public string start_time { get; set; }
		public string end_time { get; set; }
		public string interval { get; set; }
		[JsonProperty("params")]
		public Params_FA params_FA { get; set; }
	}
}
