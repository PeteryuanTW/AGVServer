using System;
using System.ComponentModel.DataAnnotations;

namespace AGVServer.Data
{
	public class AMRStatus
	{
		
		[Required]
		public string robot_id { get; set; }
		[Required]
		public string robot_name { get; set; }
        [Required]
        public int connect { get; set; }
        [Required]
		public string model { get; set; }
		[Required]
		public string task_id { get; set; }
		[Required]
		public int mode { get; set; }
		[Required]
		public int battery_percent { get; set; }
		[Required]
		public double position_x { get; set; }
		[Required]
		public double position_y { get; set; }
		[Required]
		public double position_yaw { get; set; }
		[Required]
		public DateTime last_update_time { get; set; }

		public AMRStatus(Robot robot)
		{
			robot_id = robot.robot_id;
			robot_name = robot.robot_name;
			connect = robot.connection_status;
            model = robot.model;
			task_id = robot.task_id;
			mode = robot.mode;
			battery_percent = (int)Math.Round(robot.battery_percent);
			position_x = Math.Round(robot.location.x, 3);
			position_y = Math.Round(robot.location.y, 3);
			position_yaw = Math.Round(robot.location.yaw, 3);
		}
	}
}
