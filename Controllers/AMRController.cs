using AGVServer.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AGVServer.Controllers
{
	[Route("[controller]")]
	[ApiController]
	public class AMRController : ControllerBase
	{
		[HttpGet]
		[Route("[action]")]
		public ActionResult GetAllAMRStatus()
		{
			return Ok();
		}
		[HttpGet]
		[Route("[action]")]
		public ActionResult GetAMRStatusByID(string AMRID)
		{
			return Ok(new AMRStatus() { robot_id = "robot_id", robot_name = "robot_name", model = "model", task_id = "task_id", mode = 0, battery_percent = 0, last_update_time = 0.0 });
		}
	}
}
