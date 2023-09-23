using AGVServer.Data;
using AGVServer.EFModels;
using AGVServer.Service;
using DevExpress.Utils.About;
using Microsoft.AspNetCore.Mvc;
using System.Numerics;
using System.Threading.Tasks;

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
            MesTaskDetail res = dataBufferService.GetAllTasks().FirstOrDefault(x => x.TaskNoFromMes == TaskID);
            if (res != null)
            {
                return Ok(res);
            }
            else
            {
                return Ok(null);
            }

        }
        [HttpGet]
        [Route("[action]/{TaskID}")]
        public ActionResult<string> GetBarcode([FromRoute] string TaskID)
        {
            MesTaskDetail res = dataBufferService.GetAllTasks().FirstOrDefault(x => x.TaskNoFromMes == TaskID);
            if (res == null)
            {
                return NotFound(TaskID + " not found");
            }
            else
            {
                if (res.Status != 3)
                {
                    return BadRequest(TaskID + " is not fail");
                }
                else
                {
                    (bool, string) barcodeRes = dataBufferService.GetMZYByMesNo(TaskID);
                    if (barcodeRes.Item1)
                    {
                        if (barcodeRes.Item2 == res.Barcode)
                        {
                            return BadRequest(TaskID + " is not fail");
                        }
                        else
                        {
                            return Ok(barcodeRes.Item2);
                        }
                    }
                    else
                    {
                        return BadRequest(TaskID + " is not fail");
                    }
                }

            }
        }
        [HttpGet]
        [Route("[action]/{TaskID}")]
        public ActionResult<string> GetTaskErrorCode([FromRoute] string TaskID)
        {
            string res = dataBufferService.GetTaskErrorCode(TaskID);
            return Ok(res);
        }
        #endregion



        #region Post
        /// <summary>
        /// Assign an auto/auto Task to TM Server
        /// </summary>
        /// <param name="mesTask"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("[action]")]
        public async Task<ActionResult> AssignTaskAsync([FromBody] ImesTask mesTask)
        {
            (bool, string) info = await dataBufferService.GetNewMESTask(mesTask);
            if (info.Item1)
            {
                return Ok(info.Item2);
            }
            else
            {
                return BadRequest(info.Item2);
            }
            //return Ok();


        }
        [HttpPost]
        [Route("[action]")]
        public async Task<ActionResult> AssignReviseTaskAsync([FromBody] ReviseTask reviseTask)
        {
            string str = dataBufferService.GetTaskErrorCode(reviseTask.OriginalMesTaskNo);
            if (str != "M001" && str != "M002")
            {
                return BadRequest(reviseTask.OriginalMesTaskNo + " can't not be revised");
            }
            else
            {
                (bool, string) res = dataBufferService.GetMZYByMesNo(reviseTask.OriginalMesTaskNo);
                if (!res.Item1)
                {
                    return BadRequest("no mzy at current step");
                }
                else
                {
					(bool, string) r = await dataBufferService.GetReviseTask(reviseTask);
                    if (r.Item1)
                    {
                        return Ok(res.Item2);
                    }
                    else
                    {
						return BadRequest(res.Item2);
					}
				}
            }
            //return Ok();
        }
        #endregion

        #region Delete
        [HttpDelete]
        [Route("[action]/{TaskID}")]
        public async Task<ActionResult> CalcelTask([FromRoute] string TaskID)
        {
            (bool, string) res =  await dataBufferService.CancelWIPTask(TaskID);
            if (res.Item1)
            {
                return Ok(res.Item2);
            }
            else
            {
                return BadRequest(res.Item2);
            }
        }
        #endregion
    }
}
