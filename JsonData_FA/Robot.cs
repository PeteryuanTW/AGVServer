using AGVServer.Data;

namespace AGVServer.Data
{
	public class Robot
	{
		public string robot_id { get; set; }
		public string robot_name { get; set; }
		public string model { get; set; }
		public string task_id { get; set; }
		public int mode { get; set; }
		public double battery_percent { get; set; }
		public double velocity { get; set; }
		public string map { get; set; }
		public Location location { get; set; }
		public string customized_info { get; set; }
		public double mileage { get; set; }
		public string role { get; set; }
		public string fleet_name { get; set; }
		public Capability capability { get; set; }
        public int connection_status { get; set; }
        public double last_update_time { get; set; }
	}
}
