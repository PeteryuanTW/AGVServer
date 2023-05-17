namespace AGVServer.Data
{
	public class Capability
	{
		public double speed { get; set; }
		public Size size { get; set; }
		public int payload { get; set; }
		public string artifacts_ids { get; set; }
		public string artifacts_types { get; set; }
		public string behaviours { get; set; }
		public string perceptions { get; set; }
	}
}
