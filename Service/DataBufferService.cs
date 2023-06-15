using AGVServer.Data;
using AGVServer.EFModels;
using AGVServer.JsonData_FA;
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

        public object loadin;
        public object loadout;

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

        public async Task UpdateFlowPattern()
        {
            string postfix = baseURL + "/v2/flows/definitions?fleet_name=A1&flow_name=MCTEST";
            var res = await httpClient_swarmCore.GetAsync(postfix);
            loadout = await res.Content.ReadAsStringAsync();

            postfix = baseURL + "/v2/flows/definitions?fleet_name=A1&flow_name=MCTest2";
            res = await httpClient_swarmCore.GetAsync(postfix);
            loadin = await res.Content.ReadAsStringAsync();
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
                        //update modbus value to mx(or plc to modbus) and plc class
                        //Console.WriteLine(DateTime.Now.ToString("mm:ss:ffff"));
                        foreach (PLCValueTable mxModbusIndex in plcClass.valueTables)
                        {
                            //1: plc update to modbus
                            if (mxModbusIndex.updateType)
                            {
                                (bool, bool) valFromPLC = await plcClass.ReadSingleM_MC_1E(mxModbusIndex.mxIndex);
                                mxModbusIndex.mxValue = valFromPLC.Item1;
                                mxModbusIndex.mxSuccessRead = valFromPLC.Item2;

                                bool[] modbusVals = await master.ReadCoilsAsync(1, mxModbusIndex.modbusIndex, 1);
                                bool modbusVal = modbusVals[0];

                                //mxModbusIndex.mxValue = valFromPLC.Item1;
                                //mxModbusIndex.mxSuccessRead = valFromPLC.Item2;
                                if (modbusVal != valFromPLC.Item1)//need to update
                                {

                                    await master.WriteSingleCoilAsync(1, mxModbusIndex.modbusIndex, valFromPLC.Item1);
                                    //mxModbusIndex.updateValueSuccess = true;
                                    mxModbusIndex.lastUpdateTime = DateTime.Now;
                                    Console.WriteLine(DateTime.Now.ToString("hh:mm:ss fff") + "|" + "update loader " + mxModbusIndex.mxIndex + "(" + mxModbusIndex.mxValue + ") to modbus " + mxModbusIndex.modbusIndex);

                                    //await master.WriteSingleCoilAsync(1, mxModbusIndex.modbusIndex, valFromPLC.Item1);
                                    //mxModbusIndex.updateValueSuccess = true;
                                }
                                modbusVals = await master.ReadCoilsAsync(1, mxModbusIndex.modbusIndex, 1);
                                modbusVal = modbusVals[0];
                                mxModbusIndex.modbusValue = modbusVal;

                                //valFromPLC = await plcClass.ReadSingleM_MC_1E(mxModbusIndex.mxIndex);


                            }
                            //0:modbus update to plc
                            else
                            {
                                bool[] res = await master.ReadCoilsAsync(1, mxModbusIndex.modbusIndex, 1);
                                bool valFromModbus = res[0];
                                mxModbusIndex.modbusValue = valFromModbus;

                                (bool, bool) tmp = await plcClass.ReadSingleM_MC_1E(mxModbusIndex.mxIndex);
                                if (!tmp.Item2)
                                {
                                    mxModbusIndex.mxValue = false;
                                    mxModbusIndex.mxSuccessRead = false;
                                }
                                else
                                {
                                    if (tmp.Item1 != valFromModbus)
                                    {
                                        await plcClass.WriteSingleM_MC_1E(mxModbusIndex.mxIndex, valFromModbus);
                                        mxModbusIndex.lastUpdateTime = DateTime.Now;
                                        Console.WriteLine(DateTime.Now.ToString("hh:mm:ss fff") + "|" + "update modbus " + mxModbusIndex.modbusIndex + "(" + valFromModbus + ") to loader " + mxModbusIndex.mxIndex);
                                    }
                                    else
                                    {

                                    }

                                }
                                //mxModbusIndex.updateValueSuccess = await plcClass.WriteSingleM_MC_1E(mxModbusIndex.mxIndex, valFromModbus);

                                (bool, bool) readReturnVal = await plcClass.ReadSingleM_MC_1E(mxModbusIndex.mxIndex);

                                mxModbusIndex.mxValue = readReturnVal.Item1;
                                mxModbusIndex.mxSuccessRead = readReturnVal.Item2;
                            }
                            if (mxModbusIndex.modbusValue == mxModbusIndex.mxValue && mxModbusIndex.mxSuccessRead)
                            {
                                mxModbusIndex.updateValueSuccess = true;
                            }
                            else
                            {
                                mxModbusIndex.updateValueSuccess = false;
                            }

                        }
                        //Console.WriteLine(DateTime.Now.ToString("mm:ss:ffff"));
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
                if (tmp.keepUpdate)
                {
                    await tmp.TryConnectTcp();
                }
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
            PLCClass targetStation = plcClasses.First(x => x.name == mesTask.To);
            if (targetStation == null)
            {
                return;
            }

            int baseIndex = mesTask.highOrLow ? targetStation.startIndex : targetStation.startIndex + 9;

            int x = mesTask.inOrOut ? 1 : 2;
            int y = targetStation.alignSide ? 2 : 1;
            int z = mesTask.highOrLow ? 1 : 2;

            int cmd = x * 256 + y * 16 + z;
            int stationNO = targetStation.no;

            var args = new Object();
            string flowName;
            if (mesTask.inOrOut)
            {
                flowName = "LoadInFlow";
                args = new
                {
                    start_time = "",
                    end_time = "",
                    interval = "",
                    @params = new
                    {
                        global = new
                        {
                            value_ien4r = "cmd:d301_set,targetval:" + cmd.ToString(),
                            value_V7mFM = "cmd:d305_set,targetval:" + stationNO.ToString(),

                            value_CLEEs = "cmd:m" + baseIndex.ToString() + "_set,targetval:true",
                            value_bF9IF = "cmd:m" + (baseIndex + 1).ToString() + "_get,targetval:true",
                            value_uFcP4 = "cmd:m" + (baseIndex + 2).ToString() + "_set,targetval:true",
                            value_UH6jE = "cmd:m" + (baseIndex + 3).ToString() + "_get,targetval:true",
                            //reset
                            value_cg2mg = "cmd:m" + baseIndex.ToString() + "_set,targetval:false",
                            value_i2GAu = "cmd:m" + (baseIndex + 2).ToString() + "_set,targetval:false",
                            assigned_robot = "smr_9901010201002t740351",
                        },


                    }
                };
            }
            else
            {
                flowName = "LoadOutFlow";
                args = new
                {
                    start_time = "",
                    end_time = "",
                    interval = "",
                    @params = new
                    {
                        global = new
                        {
                            value_1jnQw = "cmd:d301_set,targetval:" + cmd.ToString(),
                            value_vDszx = "cmd:d305_set,targetval:" + stationNO.ToString(),

                            value_6dcYE = "cmd:m" + (baseIndex+5).ToString() + "_set,targetval:true",
                            value_Zs4eH = "cmd:m" + (baseIndex + 6).ToString() + "_get,targetval:true",
                            value_fiCCe = "cmd:m" + (baseIndex + 7).ToString() + "_set,targetval:true",
                            //reset
                            value_W3rLo = "cmd:m" + (baseIndex+5).ToString() + "_set,targetval:false",
                            value_Qpw0n = "cmd:m" + (baseIndex+7).ToString() + "_set,targetval:false",
                            assigned_robot = "smr_9901010201002t740351",
                        },


                    }
                };
            }
            
            var finalRes = new
            {
                args
            };
            var body = JsonConvert.SerializeObject(finalRes);
            
            
            var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/"+flowName, new StringContent(body, Encoding.UTF8, "application/json"));
            
            //int modbusStartUndex = plcClasses.First(x => x.name == mesTask.To).startIndex;
            //if (!mesTask.highOrLow)//low
            //{
            //    modbusStartUndex += 9;
            //}
            ////true:loadin false:loadout
            //if (mesTask.inOrOut)
            //{

            //}
            //else
            //{
            //    var args = new
            //    {
            //        start_time = "",
            //        end_time = "",
            //        interval = "",
            //        @params = new
            //        {
            //            value_lvrxh = "cmd:m"+ (modbusStartUndex + 5).ToString()+"_set,targetval:true",
            //            value_FucmJ = "cmd:m" + (modbusStartUndex + 6).ToString() + "_get,targetval:true",
            //            value_ySQ2u = "cmd:m" + (modbusStartUndex + 7).ToString() + "_set,targetval:true",
            //            value_6qvZU= "cmd:m" + (modbusStartUndex + 5).ToString() + "_set,targetval:false",
            //            value_0qCuQ= "cmd:m" + (modbusStartUndex + 7).ToString() + "_set,targetval:false"
            //        }
            //    };

            //    var finalRes = new
            //    {
            //        args
            //    };
            //    //var body = JsonConvert.SerializeObject(finalRes);
            //    //var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/S1", new StringContent(body, Encoding.UTF8, "application/json"));
            //}
            //switch (mesTask.To)
            //{
            //    case ("SMCU01"):

            //        break;
            //    default:
            //var args = new
            //{
            //    start_time = "",
            //    end_time = "",
            //    interval = "",
            //    @params = new object()
            //};

            //var finalRes = new
            //{
            //    args
            //};

            //var body = JsonConvert.SerializeObject(finalRes);
            //var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/S1", new StringContent(body, Encoding.UTF8, "application/json"));
            //break;
            //}

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
