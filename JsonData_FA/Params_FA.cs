using Newtonsoft.Json;
namespace AGVServer.JsonData_FA
{
	public class Params_FA
	{
		public Global global { get; set; }

		[JsonProperty("node-name")]
		public NodeName nodename { get; set; }

		public string value_ien4r { get; set; }

		public string value_V7mFM { get; set; }

		public string value_CLEEs { get; set; }

		public string value_bF9IF { get; set; }

		public string value_uFcP4 { get; set; }

		public string value_UH6jE { get; set; }

		public string value_cg2mg { get; set; }

		public string value_i2GAu { get; set; }
	}
}
