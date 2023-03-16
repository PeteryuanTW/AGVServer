using AGVServer.Data;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace AGVServer.Controllers
{
	[Route("[controller]")]
	[ApiController]
	public class TaskController : ControllerBase
	{
		#region Get
		[HttpGet]
		[Route("[action]")]
		public ActionResult<List<MesTask>> GetAllTasks()
		{
			return Ok(new List<MesTask> { new MesTask() {TaskNO="001", From="start", To="destination" , SequenceNum="002", Status="waiting", Priority=0 } });
		}
		[HttpGet]
		[Route("[action]")]
		public ActionResult GetTaskByID(string TaskID)
		{
			return Ok();
		}
		[HttpGet]
		[Route("[action]")]
		public ActionResult<bool> CheckSequenceNumInTask(string barcodeRes)
		{
			return Ok(true);
		}
		#endregion



		#region Post
		[HttpPost]
		[Route("[action]")]
		public ActionResult AssignTask(MesTask mesTask)
		{
			return Ok();
		}
		[HttpPost]
		[Route("[action]")]
		public ActionResult AssignTaskToAMR(MesTask mesTask, string amrID)
		{
			return Ok();
		}
		#endregion



		#region Put
		[HttpPut]
		public ActionResult UpdateTaskStatus(string taskNO, string status)
		{
			return Ok();
		}
		#endregion


		#region Delete
		[HttpDelete]
		[Route("[action]")]
		public ActionResult RemoveTaskByID(string TaskID)
		{
			return Ok();
		}
		#endregion
	}
}
