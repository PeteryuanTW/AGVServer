using AGVServer.Data;
using AGVServer.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AGVServer.Controllers
{
	[Route("[controller]")]
	[ApiController]
	public class AMRController : ControllerBase
	{
		private readonly DataBufferService dataBufferService;

		public AMRController(DataBufferService dataBufferService)
		{
			this.dataBufferService = dataBufferService;
		}

		[HttpGet]
        [Route("[action]")]
		public ActionResult<List<AMRStatus>> GetAllAMRStatus()
		{
			return Ok(dataBufferService.GetAMRstatusList());
		}
		[HttpGet]
		[Route("[action]/{AMRID}")]
		public ActionResult<AMRStatus> GetAMRStatusByID([FromRoute] string AMRID)
		{
			AMRStatus res = dataBufferService.GetAMRstatusList().Find(x => x.robot_id == AMRID);
			if (res != null)
			{
				return Ok(res);
			}
			else
			{
				return new EmptyResult();
			}
			
		}
	}
}
