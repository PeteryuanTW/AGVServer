using AGVServer.Data;
using AGVServer.Service;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace AGVServer.Controllers
{
	[Route("[controller]")]
	[ApiController]
	public class TaskController : ControllerBase
	{
		private readonly DataBufferService dataBufferService;
		public TaskController(DataBufferService dataBufferService)
		{
			this.dataBufferService = dataBufferService;
		}




		#region Get
		[HttpGet]
		[Route("[action]")]
		public ActionResult<List<MesTask>> GetAllTasks()
		{
			return Ok(dataBufferService.GetTasks());
		}
		[HttpGet]
		[Route("[action]")]
		public ActionResult<MesTask> GetTaskByID([FromRoute] string TaskID)
		{
			return Ok(dataBufferService.GetTasksByNO(TaskID));
		}
		[HttpGet]
		[Route("[action]")]
		public ActionResult<bool> CheckSequenceNumInTask([FromRoute] string barcodeRes)
		{
			return Ok(true);
		}
		#endregion



		#region Post
		[HttpPost]
		[Route("[action]")]
		public async Task<ActionResult> AssignTaskAsync([FromBody] MesTask mesTask)
		{
			await dataBufferService.GetNewTask(mesTask);
			return Ok();
		}
		[HttpPost]
		[Route("[action]")]
		public ActionResult AssignTaskToAMR([FromRoute] MesTask mesTask, [FromRoute] string amrID)
		{
			return Ok();
		}
		#endregion



		#region Put
		[HttpPut]
		[Route("[action]")]
		public ActionResult UpdateTaskStatus([FromRoute] string taskNO, [FromRoute] string status)
		{
			return Ok();
		}
		#endregion


		#region Delete
		[HttpDelete]
		[Route("[action]")]
		public ActionResult RemoveTaskByID([FromQuery] string TaskID)
		{
			return Ok();
		}
		#endregion
	}
}
