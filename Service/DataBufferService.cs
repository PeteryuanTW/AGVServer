using AGVServer.Data;
using AGVServer.EFModels;
using DevExpress.ClipboardSource.SpreadsheetML;
using DevExpress.Utils.Filtering.Internal;
using DevExpress.XtraPrinting.Shape.Native;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NModbus;
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

		private IEnumerable<Plcconfig> plcconfigs;
		private List<PLCClass> plcClasses = new();
		private List<MxmodbusIndex> indexTable = new();

		public DataBufferService(IServiceScopeFactory scopeFactory)
		{
			if (httpClient_swarmCore == null)
			{
				httpClient_swarmCore = new HttpClient();
			}
			if (tcpClient_LocalModbusSlave == null)
			{
				tcpClient_LocalModbusSlave = new TcpClient("127.0.0.1", 502);
				factory = new ModbusFactory();
				master = factory.CreateMaster(tcpClient_LocalModbusSlave);
			}
			this._DBcontext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<AGVDBContext>();
			this.configService = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ConfigService>();
			baseURL = configService.GetURLAndPort();
			plcconfigs = configService.GetPlcConfigs();
			indexTable = _DBcontext.MxmodbusIndices.ToList();
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
				Console.WriteLine("update swarm core data fail  at " + baseURL + ":" + postfix);
			}

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
			foreach (PLCClass plcClass in plcClasses)
			{
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
						//update modbus value to mx and plc class
						foreach (PLCValueTable mxModbusIndex in plcClass.valueTables)
						{
							bool[] res = await master.ReadCoilsAsync(1, mxModbusIndex.modbusIndex, 1);
							bool valFromModbus = res[0];
							mxModbusIndex.modbusValue = valFromModbus;

							mxModbusIndex.mxSuccessWrite = await plcClass.WriteSingleM_MX(mxModbusIndex.mxIndex, valFromModbus);

							(bool, bool) readReturnVal = await plcClass.ReadSingleM_MX(mxModbusIndex.mxIndex);

							mxModbusIndex.mxValue = readReturnVal.Item1;
							mxModbusIndex.mxSuccessRead = readReturnVal.Item2;
						}
						plcClass.SelfCheck();
					}
				}


			}
			OnPLCClassesChange();
		}


		#endregion

		#region PLC
		public IEnumerable<Plcconfig> GetPLCConfigs()
		{
			return plcconfigs;
		}
		public async Task InitPLCClass()
		{
			plcClasses = await ToPLCClasses(plcconfigs);
		}

		public IEnumerable<MxmodbusIndex> GetPLCIndexTable(string type)
		{
			return indexTable;
		}

		public IEnumerable<PLCClass> GetPLCClasses()
		{
			return plcClasses;
		}

		private async Task<List<PLCClass>> ToPLCClasses(IEnumerable<Plcconfig> plcconfigs)
		{
			List<PLCClass> res = new();
			foreach (Plcconfig plcconfig in plcconfigs)
			{
				List<MxmodbusIndex> typeIndexTable = indexTable.Where(x => x.Plctype == plcconfig.Plctype).ToList();
				PLCClass tmp = new PLCClass(plcconfig, typeIndexTable);
				await tmp.TryConnectTcp();
				res.Add(tmp);
			}
			return res;
		}
		public event Action<List<PLCClass>>? PLCClassesChangeAct;
		private void OnPLCClassesChange() => PLCClassesChangeAct?.Invoke(plcClasses);
		#endregion







		//
		private List<MesTask> Tasks = new List<MesTask>();
		public List<MesTask> GetTasks()
		{
			return Tasks;
		}
		public List<MesTask> GetTasksByNO(string no)
		{
			return Tasks.Where(x => x.TaskNO == no).ToList();
		}
		public async Task GetNewTask(MesTask mesTask)
		{
			string postfix = "/v2/flows";
			if (Tasks.Exists(x => x.TaskNO == mesTask.TaskNO))
			{
				await Task.CompletedTask;
			}
			else
			{
				Tasks.Add(mesTask);
				switch ((mesTask.From, mesTask.To))
				{
					case ("a", "b"):
						break;
					default:
						var args = new
						{
							start_time = "",
							end_time = "",
							interval = "",
							@params = new object()
						};

						var finalRes = new
						{
							args
						};

						var body = JsonConvert.SerializeObject(finalRes);
						var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/S1", new StringContent(body, Encoding.UTF8, "application/json"));
						break;
				}
			}
		}



		private List<AMRStatus> AMRstatusList = new List<AMRStatus>();
		public event Action<List<AMRStatus>>? AMRstatusListChangeAct;
		private void OnAMRstatusListChange() => AMRstatusListChangeAct?.Invoke(AMRstatusList);
		public List<AMRStatus> GetAMRstatusList()
		{
			return AMRstatusList;
		}

	}
}
