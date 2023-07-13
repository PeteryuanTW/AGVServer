using AGVServer.Data;
using AGVServer.EFModels;
using AGVServer.JsonData_FA;
using DevExpress.Blazor.Internal.Grid;
using DevExpress.ClipboardSource.SpreadsheetML;
using DevExpress.Office;
using DevExpress.Pdf.Native.BouncyCastle.Asn1.X509;
using DevExpress.Utils.About;
using DevExpress.Utils.Filtering.Internal;
using DevExpress.XtraPrinting;
using DevExpress.XtraPrinting.Shape.Native;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NModbus;
using Serilog;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace AGVServer.Service
{
	public class DataBufferService
	{
		private readonly AGVDBContext _DBcontext;
		private readonly IServiceScopeFactory scopeFactory;
		private readonly HttpClient httpClient_swarmCore;
		private readonly TcpClient tcpClient_LocalModbusSlave;
		private readonly ModbusFactory factory;
		private readonly IModbusMaster master;
		private ConfigService configService;

		private string _bearerToken = "";
		private string baseURL;

		private bool swarmCoreUpdateFlag = false;
		private DateTime swarmCoreUpdateTime = DateTime.Now;

		private bool plcUpdateFlag = true;

		private IEnumerable<Plcconfig> plcconfigs;
		private List<PLCClass> plcClasses = new();
		private List<MxmodbusIndex> indexTable = new();
		private int plcRetryTimes = 3;

		private List<ManualStationConfig> manualStationConfigs = new();

		private List<MesTask> MesTasks_WIP = new();

		private Dictionary<string, (TaskStatus, string)> swarmCoreTaskStatus = new();

		public DataBufferService(IServiceScopeFactory scopeFactory)
		{
			if (httpClient_swarmCore == null)
			{
				httpClient_swarmCore = new HttpClient();
			}
			if (tcpClient_LocalModbusSlave == null)
			{
				tcpClient_LocalModbusSlave = new TcpClient("10.10.3.188", 502);
				factory = new ModbusFactory();
				master = factory.CreateMaster(tcpClient_LocalModbusSlave);
			}
			this._DBcontext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<AGVDBContext>();
			this.configService = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ConfigService>();
			baseURL = configService.GetURLAndPort();

			plcconfigs = configService.GetPlcConfigs();
			plcRetryTimes = configService.GetPLCRetryTimes();

			indexTable = _DBcontext.MxmodbusIndices.ToList();

			manualStationConfigs = _DBcontext.ManualStationConfigs.ToList();

			MesTasks_WIP = _DBcontext.MesTasks.Where(x => x.Status == 0 || x.Status == 1).ToList();
		}

		public string GetBearerToken()
		{
			return _bearerToken;
		}
		public void SetBearerToken(string token)
		{
			_bearerToken = token;
			OnBearerTokenChange();
		}
		public event Action<string>? BearerTokenChangeAct;
		private void OnBearerTokenChange() => BearerTokenChangeAct?.Invoke(_bearerToken);

		public bool GetswarmCoreUpdateFlag()
		{
			return swarmCoreUpdateFlag;
		}
		public void SetswarmCoreUpdateFlag(bool flag)
		{
			swarmCoreUpdateFlag = flag;
			swarmCoreUpdateTime = DateTime.Now;
			OnswarmCoreUpdateFlagChange();
		}
		public DateTime GetSwarmCoreUpdateTime()
		{
			return swarmCoreUpdateTime;
		}
		public event Action<bool, DateTime>? swarmCoreUpdateFlagChangeAct;
		private void OnswarmCoreUpdateFlagChange() => swarmCoreUpdateFlagChangeAct?.Invoke(swarmCoreUpdateFlag, swarmCoreUpdateTime);

		#region function for updating data from others background service
		public async Task UpdateToken()
		{
			string postfix = "/login/access-token";
			var data = new[]
			{
				new KeyValuePair<string, string>("username", "admin"),
				new KeyValuePair<string, string>("password", "admin"),
				};

			var res = await httpClient_swarmCore.PostAsync(baseURL + postfix, new FormUrlEncodedContent(data));
			var responseStr = await res.Content.ReadAsStringAsync();
			BearerToken bearerToken = Newtonsoft.Json.JsonConvert.DeserializeObject<BearerToken>(responseStr);
			SetBearerToken(bearerToken.access_token);
			httpClient_swarmCore.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);


		}


		public async Task UpdateAMRStatus()
		{
			string postfix = "/v2/robots/status";
			var res = await httpClient_swarmCore.GetAsync(baseURL + postfix);
			var responseStr = await res.Content.ReadAsStringAsync();
			try
			{
				DateTime updateTime = DateTime.Now;
				Robots robots = JsonConvert.DeserializeObject<Robots>(responseStr);
				if (robots != null)
				{
					AMRstatusList = FARobotToAMRStatus(robots, updateTime);
				}
				else
				{
					AMRstatusList = new();
				}

				OnAMRstatusListChange();
			}
			catch (Exception e)
			{
				Console.WriteLine("update swarm core data fail  at " + baseURL + ":" + postfix + "("+e.Message+")");
			}

		}

		public Dictionary<string, (TaskStatus, string)> GetSwarmCoreTaskStatus()
		{
			return swarmCoreTaskStatus;
		}
		public async Task UpdateSwarmCoreTaskStatus()
		{
			string postfix = "/v2/flows/status";
			var res = await httpClient_swarmCore.GetAsync(baseURL + postfix);
			var responseStr = await res.Content.ReadAsStringAsync();
			try
			{
				Dictionary<string, (TaskStatus, string)> tmp = new();
				var response = JObject.Parse(responseStr);
				var swarmDatas = (JArray)response["swarm_data"];
				foreach (var swarmData in swarmDatas)
				{
					string flowID = (string)swarmData["flow_id"];
					var tasks = (JArray)swarmData["tasks"];
					foreach (var taskParameter in tasks)
					{
						int state = (int)taskParameter["state"];
						string amrid = (string)taskParameter["robot_id"];
						tmp.Add(flowID, ((TaskStatus)state, amrid));
					}
					
				}
				swarmCoreTaskStatus = tmp;
			}
			catch (Exception e)
			{
				Console.WriteLine("update swarm core data fail  at " + baseURL + ":" + postfix);
			}
		}

		public enum TaskStatus
		{
			Queue = 0, Active = 1, Complete = 2, Fail = 3, Pause = 4, Cancel = 5, Unknow = 6,
		}

		private List<AMRStatus> FARobotToAMRStatus(Robots robotsList, DateTime updateTime)
		{
			List<AMRStatus> res = new();
			if (robotsList.robots == null)
			{
				return res;
			}
			foreach (Robot robot in robotsList.robots)
			{
				AMRStatus tmp = new AMRStatus(robot);
				tmp.last_update_time = updateTime;
				res.Add(tmp);
			}
			return res;
		}

		public async Task UpdatePLCStatus(IModbusMaster master)
		{

			await Task.Run(async () =>
			{
				Parallel.ForEach(plcClasses, async plcClass =>
				{
					DateTime start = DateTime.Now;
					//should not update
					if (!plcClass.keepUpdate)
					{
						if (plcClass.tcpConnect)
						{
							await plcClass.TryDisconnect();
						}
					}
					//should update
					else
					{
						if (!plcClass.tcpConnect)
						{
							plcClass.ResetValueTables();
							await plcClass.TryConnectTcp();
						}
						if (plcClass.tcpConnect)
						{
							await plcClass.SyncPLCModbus(master);
						}
					}
					OnSinglePLCClassChange(plcClass);
				});
				await Task.Delay(1);
			});
		}

		public async Task ResetModbusValue(PLCClass plcClass)
		{
			List<PLCValueTable> valueTables = plcClass.valueTables;
			if (valueTables == null || valueTables.Count == 0)
			{
				return;
			}
			else
			{
				await Task.Run(async () =>
				{
					foreach (PLCValueTable item in valueTables)
					{
						if (!item.updateType)
						{
							await master.WriteSingleCoilAsync(1, item.modbusIndex, false);
						}
					}
				});
			}
		}

		#endregion

		#region PLC

		public bool GetPLCUpdateFlag()
		{
			return plcUpdateFlag;
		}
		public void SetPLCUpdateFlag(bool flag)
		{
			plcUpdateFlag = flag;
		}
		public event Action<bool>? plcUpdateFlagChangeAct;
		private void OnPLCUpdateFlagChange() => plcUpdateFlagChangeAct?.Invoke(plcUpdateFlag);
		public IEnumerable<Plcconfig> GetPLCConfigs()
		{
			return plcconfigs;
		}
		public async Task InitPLCClass()
		{
			await ToPLCClasses(plcconfigs);
		}

		public IEnumerable<MxmodbusIndex> GetPLCIndexTable(string type)
		{
			return indexTable;
		}

		public IEnumerable<PLCClass> GetPLCClasses()
		{
			return plcClasses;
		}

		private async Task ToPLCClasses(IEnumerable<Plcconfig> plcconfigs)
		{
			plcClasses = new();
			await Task.Run(async () =>
			{
				Parallel.ForEach(plcconfigs, async plcconfig =>
				{
					List<MxmodbusIndex> typeIndexTable = indexTable.Where(x => x.Plctype == plcconfig.Plctype).ToList();
					PLCClass tmp = new PLCClass(plcconfig, typeIndexTable, plcRetryTimes);
					if (tmp.keepUpdate)
					{
						await tmp.TryConnectTcp();
					}

					plcClasses.Add(tmp);
					OnSinglePLCClassChange(tmp);
				});
				await Task.Delay(1);
			});
		}

		public event Action<PLCClass>? SinglePLCClassChangeAct;
		private void OnSinglePLCClassChange(PLCClass plcClass) => SinglePLCClassChangeAct?.Invoke(plcClass);

		public event Action<List<PLCClass>>? PLCClassesChangeAct;
		private void OnPLCClassesChange() => PLCClassesChangeAct?.Invoke(plcClasses);
		#endregion



		#region Mes
		public List<MesTask> GetAllTasks()
		{
			return _DBcontext.MesTasks.ToList();
		}
		public List<MesTask> GetWIPTasks()
		{
			return MesTasks_WIP.ToList();
		}
		public List<MesTask> GetWIPTasksByNO(string no)
		{
			return MesTasks_WIP.Where(x => x.TaskNoFromMes == no).ToList();
		}

		public List<MesTask> GetFinishedTask()
		{
			return _DBcontext.MesTasks.Where(x => x.Status == 2).ToList();
		}
		public bool GetFinishedTaskExist(string no)
		{
			return _DBcontext.MesTasks.Any(x => x.Status == 2 && x.TaskNoFromMes == no);
		}

		public async Task UpsertMesTaskForDB(MesTask mesTask)//for DB
		{
			await Task.Run(async () =>
			{
				bool taskExist = _DBcontext.MesTasks.Any(x => x.TaskNoFromMes == mesTask.TaskNoFromMes);
				if (taskExist)
				{
					MesTask target = _DBcontext.MesTasks.FirstOrDefault(x => x.TaskNoFromMes == mesTask.TaskNoFromMes);
					_DBcontext.MesTasks.Update(target);
				}
				else
				{
					await _DBcontext.MesTasks.AddAsync(mesTask);
				}
				await _DBcontext.SaveChangesAsync();
			});

		}
		public async Task UpsertMesTaskForWIP(MesTask mesTask)//status, amr id and all time logs
		{
			await Task.Run(async () =>
			{
				bool taskExist = MesTasks_WIP.Any(x => x.TaskNoFromMes == mesTask.TaskNoFromMes);
				if (taskExist)
				{
					MesTask target = MesTasks_WIP.FirstOrDefault(x => x.TaskNoFromMes == mesTask.TaskNoFromMes);
					target.Status = mesTask.Status;
					target.Amrid = mesTask.Amrid;
					target.GetFromMesTime = mesTask.GetFromMesTime;
					target.AssignToSwarmCoreTime = mesTask.AssignToSwarmCoreTime;
					target.FinishOrTimeoutTime = mesTask.FinishOrTimeoutTime;
				}
				else
				{
					MesTasks_WIP.Add(mesTask);
				}
			});
		}
		public void InitMesTask(MesTask mesTask)
		{
			mesTask.Status = 0;
			mesTask.GetFromMesTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
			mesTask.AssignToSwarmCoreTime = "";
			mesTask.SwarmCoreActualStratTime = "";
			mesTask.FinishOrTimeoutTime = "";
		}
		public void AssignMesToSwarmCore(MesTask mesTask, string swarmcoreTaskNo)
		{
			mesTask.TaskNoFromSwarmCore = swarmcoreTaskNo;
			mesTask.AssignToSwarmCoreTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
		}
		public void SwarmCoreStartProcessing(MesTask mesTask, string amr)
		{
			mesTask.Status = 1;
			mesTask.Amrid = amr;
			mesTask.SwarmCoreActualStratTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
		}
		public void SwarmCoreFinishProcessing(MesTask mesTask)
		{
			mesTask.Status = 2;
			mesTask.FinishOrTimeoutTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
		}
		public async Task GetNewTask(MesTask mesTask)
		{
			if (MesTasks_WIP.Exists(x => x.TaskNoFromMes == mesTask.TaskNoFromMes))
			{
				Log.Warning("mesTask " + mesTask.TaskNoFromMes + " from api already exist in WIP so drop it");
				return;
			}
			if (GetFinishedTaskExist(mesTask.TaskNoFromMes))
			{
				Log.Warning("mesTask " + mesTask.TaskNoFromMes + " from api already exist in DB so drop it");
				return;
			}


			await UpdateAMRStatus();//get lastest amr status
			string postfix = "/v2/flows";
			//get plc class by from & to
			PLCClass start = plcClasses.First(x => x.name.Contains(mesTask.FromStation) && x.tcpConnect);
			PLCClass destination = plcClasses.First(x => x.name.Contains(mesTask.ToStation) && x.tcpConnect);

			//get target robot
			if (start == null || destination == null)
			{
				return;
				//await Task.CompletedTask;
			}
			string startPointStr = mesTask.FromStation.ToString();
			string endPointStr = mesTask.ToStation.ToString();
			//check station from to validation
			if (start.name.Contains("STCL") || destination.name.Contains("STCU"))
			{
				await Task.CompletedTask;
			}

			string targetAmr = "";
			if (AMRstatusList.Exists(x => x.robot_id == mesTask.Amrid && x.mode == 0))
			{
				targetAmr = AMRstatusList.FirstOrDefault(x => x.robot_id == mesTask.Amrid && x.mode == 0).robot_id;
			}
			else
			{
				if (AMRstatusList.Exists(x => x.mode == 0))
				{
					targetAmr = AMRstatusList.FirstOrDefault(x => x.mode == 0).robot_id;
				}
				else
				{
					await Task.CompletedTask;
				}
			}



			//int baseIndex = mesTask.highOrLow ? targetStation.startIndex : targetStation.startIndex + 9;
			int startBaseIndex = mesTask.LoaderToAmrhighOrLow ? start.startIndex + 5 : start.startIndex + 5 + 9;
			int destBaseIndex = mesTask.AmrtoLoaderHighOrLow ? destination.startIndex : destination.startIndex + 9;

			//int x = mesTask.inOrOut ? 1 : 2;
			int x_start = 2;
			int x_dest = 1;

			//int y = targetStation.alignSide ? 2 : 1;
			int y_start = start.alignSide ? 2 : 1;
			//if (start.name.Contains("STCU"))
			//{
			//	//switch side
			//	y_start = y_start == 2 ? 1 : 2;
			//}


			int y_dest = destination.alignSide ? 2 : 1;
			if (destination.name.Contains("STCL"))
			{
				y_dest = 2;
			}

			//int z = mesTask.highOrLow ? 1 : 2;
			int z_start = mesTask.LoaderToAmrhighOrLow ? 1 : 2;
			string startPostfix = mesTask.LoaderToAmrhighOrLow ? "_up" : "_down";

			int z_dest = mesTask.AmrtoLoaderHighOrLow ? 1 : 2;
			string destPostfix = mesTask.AmrtoLoaderHighOrLow ? "_up" : "_down";

			//int cmd = x * 256 + y * 16 + z;
			int cmd_start = x_start * 256 + y_start * 16 + z_start;
			int cmd_dest = x_dest * 256 + y_dest * 16 + z_dest;

			//int stationNO = targetStation.no;
			int stationNO_start = start.name.Contains("STCU") ? start.no : start.no + 4;
			int stationNO_dest = destination.no;

			//string start_tmpPoint = start.tmpPoint;
			if (startPointStr.Contains("STCU"))
			{
				//start_tmpPoint = start_tmpPoint.Replace('L', 'U');
			}
			//string dest_tmpPoint = destination.tmpPoint;

			var args = new Object();
			args = new
			{
				start_time = "",
				end_time = "",
				interval = "",
				@params = new
				{
					global = new
					{
						//go to first loader
						//goal_1XPVc = "ADLINK_Final_1" + "@Default-area@" + start.name + "_up",
						//goal_oZ7XH = "ADLINK_Final_1" + "@Default-area@" + start.name + "_up",

						//goal_ASH5e = "ADLINK_Final_1" + "@default_area@" + start_tmpPoint,
						//goal_x6tBG = "ADLINK_Final_1" + "@default_area@" + start_tmpPoint,

						goal_kUGTd = "ADLINK_Final_1" + "@default_area@" + startPointStr + startPostfix,//start.name
						goal_qldDf = "ADLINK_Final_1" + "@default_area@" + startPointStr + startPostfix,


						//loadout
						value_1jnQw = "cmd:d301_set,targetval:" + cmd_start.ToString(),
						value_vDszx = "cmd:d305_set,targetval:" + start.no.ToString(),

						value_6dcYE = "cmd:m" + startBaseIndex.ToString() + "_set,targetval:true",
						value_Zs4eH = "cmd:m" + (startBaseIndex + 1).ToString() + "_get,targetval:true",
						value_fiCCe = "cmd:m" + (startBaseIndex + 2).ToString() + "_set,targetval:true",
						//reset
						value_W3rLo = "cmd:m" + startBaseIndex.ToString() + "_set,targetval:false",
						value_Qpw0n = "cmd:m" + (startBaseIndex + 2).ToString() + "_set,targetval:false",

						//go to second loader
						//goal_Y7aUq = "ADLINK_Final_1" + "@Default-area@" + destination.name + "_down",
						//goal_XOeLu = "ADLINK_Final_1" + "@Default-area@" + destination.name + "_down",
						//goal_b5U7t = "ADLINK_Final_1" + "@default_area@" + dest_tmpPoint,
						//goal_JQUJ0 = "ADLINK_Final_1" + "@default_area@" + dest_tmpPoint,
						goal_aPVPS = "ADLINK_Final_1" + "@default_area@" + endPointStr + destPostfix,//destination.name
						goal_huyVk = "ADLINK_Final_1" + "@default_area@" + endPointStr + destPostfix,

						//loadin
						value_ien4r = "cmd:d301_set,targetval:" + cmd_dest.ToString(),
						value_V7mFM = "cmd:d305_set,targetval:" + destination.no.ToString(),

						value_CLEEs = "cmd:m" + destBaseIndex.ToString() + "_set,targetval:true",
						value_bF9IF = "cmd:m" + (destBaseIndex + 1).ToString() + "_get,targetval:true",
						value_uFcP4 = "cmd:m" + (destBaseIndex + 2).ToString() + "_set,targetval:true",
						value_UH6jE = "cmd:m" + (destBaseIndex + 3).ToString() + "_get,targetval:true",
						//reset
						value_cg2mg = "cmd:m" + destBaseIndex.ToString() + "_set,targetval:false",
						value_i2GAu = "cmd:m" + (destBaseIndex + 2).ToString() + "_set,targetval:false",
						assigned_robot = targetAmr,
					},
				}
			};

			var finalRes = new
			{
				args
			};
			Console.WriteLine(finalRes);
			var body = JsonConvert.SerializeObject(finalRes);
			var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/MESTask", new StringContent(body, Encoding.UTF8, "application/json"));
			var respond = await res.Content.ReadAsStringAsync();
			AssignTaskDetail assignTaskDetail = JsonConvert.DeserializeObject<AssignTaskDetail>(respond);
			string flowID = assignTaskDetail.swarm_data.flow_id;

			InitMesTask(mesTask);
			Console.WriteLine(flowID);
			AssignMesToSwarmCore(mesTask, flowID);
			await UpsertMesTaskForDB(mesTask);//only insert here
			Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
			MesTasks_WIP.Add(mesTask);
			Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
			OnSingleMesTaskChange(mesTask);
		}

		public async Task<(bool, string)> GetNewMESTask(MesTask mesTask)
		{
			Log.Information("get mesTask " + mesTask.TaskNoFromMes);
			//auto assign task no. with time
			if (mesTask.TaskNoFromMes == "string")
			{
				mesTask.TaskNoFromMes = "auto_" + DateTime.Now.ToString("yyyyMMddHHmmss");
			}
			if (MesTasks_WIP.Exists(x => x.TaskNoFromMes == mesTask.TaskNoFromMes))
			{
				string info = "mesTask " + mesTask.TaskNoFromMes + " from api already exist in WIP so drop it";
				Log.Warning(info);
				return (false, info);
			}
			if (GetFinishedTaskExist(mesTask.TaskNoFromMes))
			{
				string info = "mesTask " + mesTask.TaskNoFromMes + " from api already exist in DB so drop it";
				Log.Warning(info);
				return (false, info);
			}
			bool returnFlag = false;
			string returnStr = "fali before assigning task " + mesTask.TaskNoFromMes;
			string postfix = "/v2/flows";
			switch (mesTask.TaskType)
			{
				//auto to auto
				case 0:
					{

						//get plc class by from & to
						PLCClass start = plcClasses.First(x => x.name.Contains(mesTask.FromStation) && x.tcpConnect);
						PLCClass destination = plcClasses.First(x => x.name.Contains(mesTask.ToStation) && x.tcpConnect);
						if (start == null || destination == null)
						{
							return (false, "check loader station exist and connected");
						}
						Plcconfig start_config = plcconfigs.First(x => x.Name.Contains(mesTask.FromStation));
						Plcconfig dest_config = plcconfigs.First(x => x.Name.Contains(mesTask.ToStation));
						if (start_config == null || dest_config == null)
						{
							return (false, "check plc config");
						}
						//check station from to validation
						if (start.name.Contains("STCL") || destination.name.Contains("STCU"))
						{
							return (false, "can't start from STCL or to STCU");
						}

						bool sameArea = start_config.ArtifactId == dest_config.ArtifactId ? true : false;

						//start point parameter
						string startPointStr = mesTask.LoaderToAmrhighOrLow ? mesTask.FromStation.ToString() + "_up" : mesTask.FromStation.ToString() + "_down";

						string startGateInCell = start_config.GateInCell != "" ? start_config.GateInCell : startPointStr;
						string startArtifactID = start_config.ArtifactId;
						string startInOperation = startArtifactID == "artifact_39578" ? "pass" : "enter";
						string startOutOperation = startArtifactID == "artifact_39578" ? "pass" : "leave";
						if (sameArea)
						{
							startOutOperation = "stay";
						}
						string startRotateCell = start_config.RotateCell != "" ? start_config.RotateCell : startPointStr;
						string startRotateDegree = start_config.RotateDegree;
						string startRotateTarget = start_config.RotateDest;

						string startGateOutCell = start_config.GateOutCell != "" ? start_config.GateOutCell : startPointStr;
						if (sameArea)
						{
							startGateOutCell = startPointStr;
						}

						string areaTemporary = sameArea ? "default_area" : "gate";

						//destination point parameter
						string endPointStr = mesTask.AmrtoLoaderHighOrLow ? mesTask.ToStation.ToString() + "_up" : mesTask.ToStation.ToString() + "_down";

						string destGateInCell = dest_config.GateInCell != "" ? dest_config.GateInCell : endPointStr;
						if (sameArea)
						{
							destGateInCell = endPointStr;
						}

						string destArtifactID = dest_config.ArtifactId;
						string destInOperation = destArtifactID == "artifact_39578" ? "pass" : "enter";
						if (sameArea)
						{
							destInOperation = "stay";
						}
						string destOutOperation = destArtifactID == "artifact_39578" ? "pass" : "leave";

						string destRotateCell = dest_config.RotateCell != "" ? dest_config.RotateCell : endPointStr;
						string destRotateDegree = dest_config.RotateDegree;
						string destRotateTarget = dest_config.RotateDest;

						string destGateOutCell = dest_config.GateOutCell != "" ? dest_config.GateOutCell : endPointStr;



						int startBaseIndex = mesTask.LoaderToAmrhighOrLow ? start.startIndex + 5 : start.startIndex + 5 + 9;
						int destBaseIndex = mesTask.AmrtoLoaderHighOrLow ? destination.startIndex : destination.startIndex + 9;

						int x_start = 2;
						int x_dest = 1;

						int y_start = start.alignSide ? 2 : 1;
						int y_dest = destination.alignSide ? 2 : 1;
						if (destination.name.Contains("STCL"))
						{
							y_dest = 2;
						}

						int z_start = mesTask.LoaderToAmrhighOrLow ? 1 : 2;
						string startPostfix = mesTask.LoaderToAmrhighOrLow ? "_up" : "_down";

						int z_dest = mesTask.AmrtoLoaderHighOrLow ? 1 : 2;
						string destPostfix = mesTask.AmrtoLoaderHighOrLow ? "_up" : "_down";

						int cmd_start = x_start * 256 + y_start * 16 + z_start;
						int cmd_dest = x_dest * 256 + y_dest * 16 + z_dest;

						int stationNO_start = start.name.Contains("STCU") ? start.no : start.no + 4;
						int stationNO_dest = destination.no;

						var args = new
						{
							start_time = "",
							end_time = "",
							interval = "",
							@params = new
							{
								global = new
								{
									//write barcode first
									value_1RJzO = "cmd:setBarcode,targetval:" + mesTask.Barcode,

									//first handshake
									//enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
									goal_neRCo = "ADLINK_Final_1" + "@gate@" + startGateInCell,

									artifact_id_tUxuD = startArtifactID,
									value_tUxuD = "operation:" + startInOperation,

									goal_RgcuQ = "ADLINK_Final_1" + "@default_area@" + startRotateCell,

									angle_8zvcZ = startRotateDegree,
									goal_8zvcZ = "ADLINK_Final_1" + "@default_area@" + startRotateTarget,

									goal_BYw8v = "ADLINK_Final_1" + "@default_area@" + startPointStr,

									value_dMBsM = "cmd:m300_get,targetval:true",
									goal_dMBsM = "ADLINK_Final_1" + "@default_area@" + startPointStr,

									//loadout loader -> amr
									value_jCaeJ = "cmd:d305_set,targetval:" + start.no.ToString(),
									value_OvAy3 = "cmd:d301_set,targetval:" + cmd_start.ToString(),

									value_BPZx0 = "cmd:m" + startBaseIndex.ToString() + "_set,targetval:true",
									value_KINN5 = "cmd:m" + (startBaseIndex + 1).ToString() + "_get,targetval:true",
									value_X0Id9 = "cmd:m" + (startBaseIndex + 2).ToString() + "_set,targetval:true",
									//reset mc
									value_lRGQd = "cmd:m" + startBaseIndex.ToString() + "_set,targetval:false",
									value_6TJ2r = "cmd:m" + (startBaseIndex + 2).ToString() + "_set,targetval:false",

									//leave gate
									goal_gfxRJ = "ADLINK_Final_1" + "@"+ areaTemporary + "@" + startGateOutCell,

									artifact_id_zH5Z6 = startArtifactID,
									value_zH5Z6 = "operation:" + startOutOperation,



									//second handshake
									//enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
									goal_NST2k = "ADLINK_Final_1" + "@" + areaTemporary + "@" + destGateInCell,

									artifact_id_8Nxs7 = destArtifactID,
									value_8Nxs7 = "operation:" + destInOperation,

									goal_gT5bw = "ADLINK_Final_1" + "@default_area@" + destRotateCell,

									goal_6IlCp = "ADLINK_Final_1" + "@default_area@" + destRotateTarget,
									angle_6IlCp = destRotateDegree,

									goal_mtxL2 = "ADLINK_Final_1" + "@default_area@" + endPointStr,

									value_8S55v = "cmd:m300_get,targetval:true",
									goal_8S55v = "ADLINK_Final_1" + "@default_area@" + endPointStr,

									//loadin amr -> loader
									value_OlJhC = "cmd:d305_set,targetval:" + destination.no.ToString(),
									value_FwGMV = "cmd:d301_set,targetval:" + cmd_dest.ToString(),

									value_6VhPp = "cmd:m" + destBaseIndex.ToString() + "_set,targetval:true",
									value_bsiOr = "cmd:m" + (destBaseIndex + 1).ToString() + "_get,targetval:true",
									value_N6jTO = "cmd:m" + (destBaseIndex + 2).ToString() + "_set,targetval:true",
									value_Va2Po = "cmd:m" + (destBaseIndex + 3).ToString() + "_get,targetval:true",
									//reset mc
									value_U5zKs = "cmd:m" + destBaseIndex.ToString() + "_set,targetval:false",
									value_3RmSJ = "cmd:m" + (destBaseIndex + 2).ToString() + "_set,targetval:false",

									//leave gate
									goal_XKQwi = "ADLINK_Final_1" + "@gate@" + destGateOutCell,

									artifact_id_NuRx0 = destArtifactID,
									value_NuRx0 = "operation:" + destOutOperation,

									assigned_robot = "smr_9901010201002t73igf1",
								}
							}
						};
						var finalRes = new
						{
							args
						};

						var body = JsonConvert.SerializeObject(finalRes);
						Console.WriteLine(body);



						//send api to swarmcore
						if (!mesTask.TaskNoFromMes.Trim().Contains("test"))
						{
							//AssignTaskDetail assignTaskDetail = JsonConvert.DeserializeObject<AssignTaskDetail>(respond);
							//string flowID = assignTaskDetail.swarm_data.flow_id;

							//var responseStr = await res.Content.ReadAsStringAsync();
							var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/auto_auto_diff_flow", new StringContent(body, Encoding.UTF8, "application/json"));
							var respond = await res.Content.ReadAsStringAsync();
							try
							{
								var response = JObject.Parse(respond);
								int statusCode = (int)response["system_status_code"];
								string msg = (string)response["system_message"];
								if ((statusCode == 5550000 || statusCode == 200))
								{
									var data = (JObject)response["swarm_data"];
									string flowID = (string)data["flow_id"];

									InitMesTask(mesTask);
									Console.WriteLine(flowID);
									AssignMesToSwarmCore(mesTask, flowID);
									await UpsertMesTaskForDB(mesTask);//only insert here
									Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
									MesTasks_WIP.Add(mesTask);
									Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
									OnSingleMesTaskChange(mesTask);
									return (true, "assign success");
								}
								else
								{
									Log.Information("assign mesTask " + mesTask.TaskNoFromMes + " fail");
									return (false, "error occured when assigning to swarmcore");
								}
							}
							catch (Exception e)
							{
								Log.Information("assign mesTask " + mesTask.TaskNoFromMes + " fail");
								return (false, "error occured when assigning to swarmcore");
							}
						}
						//test data
						else
						{
							InitMesTask(mesTask);
							await UpsertMesTaskForDB(mesTask);//only insert here
							Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
							MesTasks_WIP.Add(mesTask);
							Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
							OnSingleMesTaskChange(mesTask);
							return (true, "testing assign success");
						}
					}
					break;
				//manual to auto
				case 1:
					{
						//get plc class by from & to
						PLCClass destination = plcClasses.First(x => x.name.Contains(mesTask.ToStation) && x.tcpConnect);
						if (destination == null)
						{
							return (false, "check loader station exist and connected");
						}
						ManualStationConfig start_config = manualStationConfigs.First(x => x.Name.Contains(mesTask.FromStation));
						Plcconfig dest_config = plcconfigs.First(x => x.Name.Contains(mesTask.ToStation));
						if (start_config == null || dest_config == null)
						{
							return (false, "check plc config");
						}
						if (destination.name.Contains("STCU"))
						{
							return (false, "can't send to STCU");
						}

						//manual start station
						string startPointStr = mesTask.FromStation;

						string startGateInCell = start_config.GateInCell != "" ? start_config.GateInCell : startPointStr;
						string startArtifactID = start_config.ArtifactId;
						string startInOperation = startArtifactID == "artifact_39578" ? "pass" : "enter";
						string startOutOperation = startArtifactID == "artifact_39578" ? "pass" : "leave";

						string startRotateCell = start_config.RotateCell != "" ? start_config.RotateCell : startPointStr;
						string startRotateDegree = start_config.RotateDegree;
						string startRotateTarget = start_config.RotateDest;

						string startGateOutCell = start_config.GateOutCell != "" ? start_config.GateOutCell : startPointStr;

						//auto destination station
						string endPointStr = mesTask.AmrtoLoaderHighOrLow ? mesTask.ToStation.ToString() + "_up" : mesTask.ToStation.ToString() + "_down";

						string destGateInCell = dest_config.GateInCell != "" ? dest_config.GateInCell : endPointStr;
						string destArtifactID = dest_config.ArtifactId;
						string destInOperation = destArtifactID == "artifact_39578" ? "pass" : "enter";
						string destOutOperation = destArtifactID == "artifact_39578" ? "pass" : "leave";

						string destRotateCell = dest_config.RotateCell != "" ? dest_config.RotateCell : endPointStr;
						string destRotateDegree = dest_config.RotateDegree;
						string destRotateTarget = dest_config.RotateDest;

						string destGateOutCell = dest_config.GateOutCell != "" ? dest_config.GateOutCell : endPointStr;


						//dest parameter
						int destBaseIndex = mesTask.AmrtoLoaderHighOrLow ? destination.startIndex : destination.startIndex + 9;

						int x_dest = 1;

						int y_dest = destination.alignSide ? 2 : 1;
						if (destination.name.Contains("STCL"))
						{
							y_dest = 2;
						}

						int z_dest = mesTask.AmrtoLoaderHighOrLow ? 1 : 2;
						string destPostfix = mesTask.AmrtoLoaderHighOrLow ? "_up" : "_down";

						int cmd_dest = x_dest * 256 + y_dest * 16 + z_dest;

						int stationNO_dest = destination.no;

						var args = new
						{
							start_time = "",
							end_time = "",
							interval = "",
							@params = new
							{
								global = new
								{
									//write barcode first
									value_rTj5w = "cmd:setBarcode,targetval:" + mesTask.Barcode,

									//first handshake
									//enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
									goal_akw4l = "ADLINK_Final_1" + "@gate@" + startGateInCell,

									artifact_id_i6J0T = startArtifactID,
									value_i6J0T = "operation:" + startInOperation,

									goal_UX8sH = "ADLINK_Final_1" + "@default_area@" + startRotateCell,

									angle_msOdA = startRotateDegree,
									goal_msOdA = "ADLINK_Final_1" + "@default_area@" + startRotateTarget,

									goal_hSU2V = "ADLINK_Final_1" + "@default_area@" + startPointStr,

									value_FbWUK = "cmd:m300_get,targetval:true",
									goal_FbWUK = "ADLINK_Final_1" + "@default_area@" + startPointStr,

									//set station
									value_yi1qe = "cmd:d305_set,targetval:" + start_config.No.ToString(),

									//leave gate
									goal_ys26n = "ADLINK_Final_1" + "@gate@" + startGateOutCell,

									artifact_id_mbuRS = startArtifactID,
									value_mbuRS = "operation:" + startOutOperation,



									//handshake
									//enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
									goal_SjyCS = "ADLINK_Final_1" + "@gate@" + destGateInCell,

									artifact_id_5q4C4 = destArtifactID,
									value_5q4C4 = "operation:" + destInOperation,

									goal_VBozh = "ADLINK_Final_1" + "@default_area@" + destRotateCell,

									goal_UcrvG = "ADLINK_Final_1" + "@default_area@" + destRotateTarget,
									angle_UcrvG = destRotateDegree,

									goal_KH3a0 = "ADLINK_Final_1" + "@default_area@" + endPointStr,

									value_JKiPz = "cmd:m300_get,targetval:true",
									goal_JKiPz = "ADLINK_Final_1" + "@default_area@" + endPointStr,

									//loadin amr -> loader
									value_FK7w0 = "cmd:d305_set,targetval:" + destination.no.ToString(),
									value_sFeLC = "cmd:d301_set,targetval:" + cmd_dest.ToString(),


									value_T7elM = "cmd:m" + destBaseIndex.ToString() + "_set,targetval:true",
									value_l3QjN = "cmd:m" + (destBaseIndex + 1).ToString() + "_get,targetval:true",
									value_jl6xR = "cmd:m" + (destBaseIndex + 2).ToString() + "_set,targetval:true",
									value_peIzz = "cmd:m" + (destBaseIndex + 3).ToString() + "_get,targetval:true",
									//reset mc
									value_B1xkL = "cmd:m" + destBaseIndex.ToString() + "_set,targetval:false",
									value_WW6pG = "cmd:m" + (destBaseIndex + 2).ToString() + "_set,targetval:false",

									//leave gate
									goal_R4APq = "ADLINK_Final_1" + "@gate@" + destGateOutCell,

									artifact_id_iHk6v = destArtifactID,
									value_iHk6v = "operation:" + destOutOperation,

								}
							}
						};
						var finalRes = new
						{
							args
						};

						var body = JsonConvert.SerializeObject(finalRes);
						Console.WriteLine(body);

						//send api to swarmcore
						if (!mesTask.TaskNoFromMes.Trim().Contains("test"))
						{
							var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/manual_auto_flow", new StringContent(body, Encoding.UTF8, "application/json"));
							var respond = await res.Content.ReadAsStringAsync();
							try
							{
								var response = JObject.Parse(respond);
								int statusCode = (int)response["system_status_code"];
								string msg = (string)response["system_message"];
								if ((statusCode == 5550000 || statusCode == 200) && msg == "OK")
								{
									var data = (JObject)response["swarm_data"];
									string flowID = (string)data["flow_id"];

									InitMesTask(mesTask);
									//Console.WriteLine(flowID);
									AssignMesToSwarmCore(mesTask, flowID);
									await UpsertMesTaskForDB(mesTask);//only insert here
									Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
									MesTasks_WIP.Add(mesTask);
									Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
									OnSingleMesTaskChange(mesTask);
									return (true, "assign success");
								}
								else
								{
									Log.Information("assign mesTask " + mesTask.TaskNoFromMes + " fail");
									return (false, "error occured when assigning to swarmcore");
								}
							}
							catch (Exception e)
							{
								Log.Information("assign mesTask " + mesTask.TaskNoFromMes + " fail");
								return (false, "error occured when assigning to swarmcore");
							}
						}
						//test data
						else
						{
							InitMesTask(mesTask);
							await UpsertMesTaskForDB(mesTask);//only insert here
							Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
							MesTasks_WIP.Add(mesTask);
							Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
							OnSingleMesTaskChange(mesTask);
							return (true, "testing assign success");
						}
					}
					break;
				//auto to manual
				case 2:
					{
						//get plc class by from & to
						PLCClass start = plcClasses.First(x => x.name.Contains(mesTask.FromStation) && x.tcpConnect);
						if (start == null)
						{
							return (false, "check loader station exist and connected");
						}
						Plcconfig start_config = plcconfigs.First(x => x.Name.Contains(mesTask.FromStation));
						ManualStationConfig dest_config = manualStationConfigs.First(x => x.Name.Contains(mesTask.ToStation));
						if (start_config == null || dest_config == null)
						{
							return (false, "check plc config");
						}
						if (start.name.Contains("STCL"))
						{
							return (false, "can't start from STCL");
						}
						//auto from station
						string startPointStr = mesTask.LoaderToAmrhighOrLow ? mesTask.FromStation.ToString() + "_up" : mesTask.FromStation.ToString() + "_down";

						string startGateInCell = start_config.GateInCell != "" ? start_config.GateInCell : startPointStr;
						string startArtifactID = start_config.ArtifactId;
						string startInOperation = startArtifactID == "artifact_39578" ? "pass" : "enter";
						string startOutOperation = startArtifactID == "artifact_39578" ? "pass" : "leave";

						string startRotateCell = start_config.RotateCell != "" ? start_config.RotateCell : startPointStr;
						string startRotateDegree = start_config.RotateDegree;
						string startRotateTarget = start_config.RotateDest;

						string startGateOutCell = start_config.GateOutCell != "" ? start_config.GateOutCell : startPointStr;



						//manual destination station
						string endPointStr = mesTask.ToStation;

						string destGateInCell = dest_config.GateInCell != "" ? dest_config.GateInCell : endPointStr;
						string destArtifactID = dest_config.ArtifactId;
						string destInOperation = destArtifactID == "artifact_39578" ? "pass" : "enter";
						string destOutOperation = destArtifactID == "artifact_39578" ? "pass" : "leave";

						string destRotateCell = dest_config.RotateCell != "" ? dest_config.RotateCell : endPointStr;
						string destRotateDegree = dest_config.RotateDegree;
						string destRotateTarget = dest_config.RotateDest;

						string destGateOutCell = dest_config.GateOutCell != "" ? dest_config.GateOutCell : endPointStr;



						//start auto station parameter
						int startBaseIndex = mesTask.LoaderToAmrhighOrLow ? start.startIndex + 5 : start.startIndex + 5 + 9;

						int x_start = 2;

						int y_start = start.alignSide ? 2 : 1;

						int z_start = mesTask.LoaderToAmrhighOrLow ? 1 : 2;
						string startPostfix = mesTask.LoaderToAmrhighOrLow ? "_up" : "_down";

						int cmd_start = x_start * 256 + y_start * 16 + z_start;

						int stationNO_start = start.name.Contains("STCU") ? start.no : start.no + 4;



						var args = new
						{
							start_time = "",
							end_time = "",
							interval = "",
							@params = new
							{
								global = new
								{
									//write barcode first
									value_krG4K = "cmd:setBarcode,targetval:" + mesTask.Barcode,

									//first handshake
									//enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
									goal_l50AK = "ADLINK_Final_1" + "@gate@" + startGateInCell,

									artifact_id_JcTlC = startArtifactID,
									value_JcTlC = "operation:" + startInOperation,

									goal_uCX2t = "ADLINK_Final_1" + "@default_area@" + startRotateCell,

									angle_mgYqR = startRotateDegree,
									goal_mgYqR = "ADLINK_Final_1" + "@default_area@" + startRotateTarget,

									goal_CDQ4v = "ADLINK_Final_1" + "@default_area@" + startPointStr,

									artifact_value = "cmd:m300_get,targetval:true",
									goal_7i3Yd = "ADLINK_Final_1" + "@default_area@" + startPointStr,


									//loadout loader -> amr
									value_M2UPj = "cmd:d305_set,targetval:" + start.no.ToString(),
									value_to0hg = "cmd:d301_set,targetval:" + cmd_start.ToString(),

									value_0LaFz = "cmd:m" + startBaseIndex.ToString() + "_set,targetval:true",
									value_kIdBa = "cmd:m" + (startBaseIndex + 1).ToString() + "_get,targetval:true",
									value_6eSYK = "cmd:m" + (startBaseIndex + 2).ToString() + "_set,targetval:true",
									//reset mc
									value_rJQIw = "cmd:m" + startBaseIndex.ToString() + "_set,targetval:false",
									value_zB6On = "cmd:m" + (startBaseIndex + 2).ToString() + "_set,targetval:false",

									//leave gate
									goal_Dp4xX = "ADLINK_Final_1" + "@gate@" + startGateOutCell,

									artifact_id_jAQf0 = startArtifactID,
									value_jAQf0 = "operation:" + startOutOperation,



									//enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
									goal_UvAQD = "ADLINK_Final_1" + "@gate@" + destGateInCell,

									artifact_id_279vX = destArtifactID,
									value_279vX = "operation:" + destInOperation,

									goal_8GkDv = "ADLINK_Final_1" + "@default_area@" + destRotateCell,

									goal_4sGL0 = "ADLINK_Final_1" + "@default_area@" + destRotateTarget,
									angle_4sGL0 = destRotateDegree,

									goal_NGivg = "ADLINK_Final_1" + "@default_area@" + endPointStr,

									value_HLlqM = "cmd:m300_get,targetval:true",
									goal_HLlqM = "ADLINK_Final_1" + "@default_area@" + endPointStr,

									//set station no
									value_27E0Y = "cmd:d305_set,targetval:" + dest_config.No.ToString(),

									//leave gate
									goal_xRCOG = "ADLINK_Final_1" + "@gate@" + destGateOutCell,

									artifact_id_zoI1l = destArtifactID,
									value_zoI1l = "operation:" + destOutOperation,
								}
							}
						};
						var finalRes = new
						{
							args
						};

						var body = JsonConvert.SerializeObject(finalRes);
						Console.WriteLine(body);

						//send api to swarmcore
						if (!mesTask.TaskNoFromMes.Trim().Contains("test"))
						{
							var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/auto_manual_flow", new StringContent(body, Encoding.UTF8, "application/json"));
							var respond = await res.Content.ReadAsStringAsync();
							try
							{
								var response = JObject.Parse(respond);
								int statusCode = (int)response["system_status_code"];
								string msg = (string)response["system_message"];
								if ((statusCode == 5550000 || statusCode == 200) && msg == "OK")
								{
									var data = (JObject)response["swarm_data"];
									string flowID = (string)data["flow_id"];

									InitMesTask(mesTask);
									Console.WriteLine(flowID);
									AssignMesToSwarmCore(mesTask, flowID);
									await UpsertMesTaskForDB(mesTask);//only insert here
									Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
									MesTasks_WIP.Add(mesTask);
									Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
									OnSingleMesTaskChange(mesTask);
									return (true, "assign success");
								}
								else
								{
									Log.Information("assign mesTask " + mesTask.TaskNoFromMes + " fail");
									return (false, "error occured when assigning to swarmcore");
								}
							}
							catch (Exception e)
							{
								Log.Information("assign mesTask " + mesTask.TaskNoFromMes + " fail");
								return (false, "error occured when assigning to swarmcore");
							}
						}
						//test data
						else
						{
							InitMesTask(mesTask);
							await UpsertMesTaskForDB(mesTask);//only insert here
							Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
							MesTasks_WIP.Add(mesTask);
							Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
							OnSingleMesTaskChange(mesTask);
							return (true, "testing assign success");
						}
					}
					break;
				default:
					returnFlag = false;
					returnStr = "there is only 0, 1 and 2 for tasktype";
					break;
			}
			return (returnFlag, returnStr);
		}

		public async Task UpdateWIPMesTaskStatus()
		{
			await UpdateSwarmCoreTaskStatus();
			await Task.Run(() =>
			{
				Parallel.ForEach(MesTasks_WIP, async mesTask_WIP =>
				{
					if (swarmCoreTaskStatus.Any(x => x.Key == mesTask_WIP.TaskNoFromSwarmCore && (int)x.Value.Item1 != mesTask_WIP.Status))
					{
						KeyValuePair<string, (TaskStatus, string)> mesTaskState = swarmCoreTaskStatus.FirstOrDefault(x => x.Key == mesTask_WIP.TaskNoFromSwarmCore);
						mesTask_WIP.Status = (int)mesTaskState.Value.Item1;
						switch ((int)mesTaskState.Value.Item1)
						{
							case 0:
								break;
							case 1:
								string amr = mesTaskState.Value.Item2;
								SwarmCoreStartProcessing(mesTask_WIP, amr);
								Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " assign to SwarmCore by background service");
								break;
							case 2:
								await RemoveFromWIP(mesTask_WIP);
								Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " completed and removed from WIP by Swarmcore");
								break;
							case 3:
								break;
							case 4:
								break;
							case 5:
								await RemoveFromWIP(mesTask_WIP);
								Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " is canceled removed from WIP by Swarmcore");
								break;
							default:
								break;
						}
					}
					else
					{
						await RemoveFromWIP(mesTask_WIP);
						Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " remove from WIP by background service");
					}
					OnSingleMesTaskChange(mesTask_WIP);
				});
			});
		}

		public async Task SetWIPFail(MesTask mesTask_WIP)
		{
			if (mesTask_WIP.Status != 1 && mesTask_WIP.FinishOrTimeoutTime.Trim() != "")
			{
				return;
			}
			mesTask_WIP.Status = 3;
			mesTask_WIP.FinishOrTimeoutTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

			OnSingleMesTaskChange(mesTask_WIP);
			MesTasks_WIP.Remove(mesTask_WIP);
			await UpsertMesTaskForDB(mesTask_WIP);
		}

		public async Task RemoveFromWIP(MesTask mesTask_WIP)
		{
			if (mesTask_WIP.Status != 2)
			{
				mesTask_WIP.Status = 2;
			}
			if (mesTask_WIP.FinishOrTimeoutTime.Trim() == "")
			{
				mesTask_WIP.FinishOrTimeoutTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
			}
			OnSingleMesTaskChange(mesTask_WIP);
			MesTasks_WIP.Remove(mesTask_WIP);
			await UpsertMesTaskForDB(mesTask_WIP);
		}

		public event Action<MesTask>? SingleMesTaskChangeAct;
		private void OnSingleMesTaskChange(MesTask mesTask) => SingleMesTaskChangeAct?.Invoke(mesTask);
		#endregion

		private List<AMRStatus> AMRstatusList = new List<AMRStatus>();
		public event Action<List<AMRStatus>>? AMRstatusListChangeAct;
		private void OnAMRstatusListChange() => AMRstatusListChangeAct?.Invoke(AMRstatusList);
		public List<AMRStatus> GetAMRstatusList()
		{
			return AMRstatusList;
		}

	}
}
