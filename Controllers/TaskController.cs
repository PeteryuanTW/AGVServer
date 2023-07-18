using AGVServer.Data;
using AGVServer.EFModels;
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
		public ActionResult<List<MesTaskDetail>> GetAllTasks()
		{
			return Ok(dataBufferService.GetAllTasks());
		}
		[HttpGet]
		[Route("[action]/{TaskID}")]
		public ActionResult<MesTaskDetail> GetTaskByID([FromRoute] string TaskID)
		{
			return Ok(dataBufferService.GetAllTasks().FirstOrDefault(x=>x.TaskNoFromMes == TaskID));
		}
		#endregion



		#region Post
		[HttpPost]
		[Route("[action]")]
		public async Task<ActionResult> AssignTaskAsync([FromBody] ImesTask mesTask)
		{
			//await dataBufferService.GetNewTaskTest(mesTask);
			//await dataBufferService.GetNewTask(mesTask);
			(bool, string) info =  await dataBufferService.GetNewMESTask(mesTask);
			if (info.Item1)
			{
				return Ok(info.Item2);
			}
			else
			{
				return BadRequest(info.Item2);
			}
			
		}
		#endregion



		#region Put
		[HttpPut]
		[Route("[action]/{taskNO}/{status}")]
		public ActionResult UpdateTaskStatus([FromRoute] string taskNO, [FromRoute] string status)
		{
			return Ok();
		}
		#endregion


		#region Delete
		[HttpDelete]
		[Route("[action]/{TaskID}")]
		public ActionResult RemoveTaskByID([FromRoute] string TaskID)
		{
			return Ok();
		}
		#endregion
	}
}
