using System.ComponentModel.DataAnnotations;

namespace AGVServer.Data
{
	public class AMRStatus
	{
		
		[Required]
		/// <summary>
		/// Temperature in celcius
		/// </summary>
		public string robot_id { get; set; }
		[Required]
		public string robot_name { get; set; }
		[Required]
		public string model { get; set; }
		[Required]
		public string task_id { get; set; }
		[Required]
		public int mode { get; set; }
		[Required]
		public int battery_percent { get; set; }
		[Required]
		public double last_update_time { get; set; }
	}
}
