﻿using AGVServer.Data;
using AGVServer.EFModels;
using AGVServer.JsonData_FA;
using AGVServer.Pages;
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
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

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

        private IEnumerable<CellConfig> cellConfigs;

        private List<ManualStationConfig> manualStationConfigs = new();

        private List<ImesTask> scheduleTasks = new();
        private bool scheduling = false;
        private bool schedulingPause = false;

        private List<MesTaskDetail> MesTasks_WIP = new();

        //private Dictionary<string, (TaskStatus, string)> swarmCoreTaskStatus = new();
        private List<FlowTaskStatus> swarmCoreTaskStatus = new();

        //private List<GroupClass> groups;

        private List<ImesTask> queueForGroup;

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
            this.scopeFactory = scopeFactory;
            this._DBcontext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<AGVDBContext>();
            this.configService = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ConfigService>();
            baseURL = configService.GetURLAndPort();

            plcconfigs = configService.GetPlcConfigs();
            plcRetryTimes = configService.GetPLCRetryTimes();

            indexTable = _DBcontext.MxmodbusIndices.ToList();

            cellConfigs = _DBcontext.CellConfigs.ToList();
            manualStationConfigs = _DBcontext.ManualStationConfigs.ToList();

            MesTasks_WIP = _DBcontext.MesTaskDetails.Where(x => x.Status == 0 || x.Status == 1 || x.Status == 3 || x.Status == 4).ToList();

            //RefreshSchedulingTask();

            //IEnumerable<GroupConfig> groupConfigs = _DBcontext.GroupConfigs.ToList();
            //groups = new();
            //foreach (GroupConfig groupConfig in groupConfigs)
            //{
            //	groups.Add(new GroupClass(groupConfig));
            //}

            //queueForGroup = new();
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

        //public List<GroupClass> GetGroup()
        //{
        //	return groups;
        //}

        public async Task UpdateAMRStatus()
        {
            string postfix = "/v2/fleets/status?fleet_name=A1";
            var res = await httpClient_swarmCore.GetAsync(baseURL + postfix);
            var responseStr = await res.Content.ReadAsStringAsync();
            try
            {
                DateTime updateTime = DateTime.Now;
                Fleetstates robots = JsonConvert.DeserializeObject<Fleetstates>(responseStr);
                FleetState targetFleet = robots.fleet_state[0];

                if (robots != null)
                {
                    AMRstatusList = FARobotToAMRStatus(targetFleet, updateTime);
                }
                else
                {
                    AMRstatusList = new();
                }

                OnAMRstatusListChange();
            }
            catch (Exception e)
            {
                Console.WriteLine("update swarm core data fail  at " + baseURL + ":" + postfix + "(" + e.Message + ")");
                await UpdateToken();
                Console.WriteLine("retry get token by amr status");
            }

        }

        public List<FlowTaskStatus> GetSwarmCoreTaskStatus()
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
                //Dictionary<string, (TaskStatus, string)> tmp = new();
                List<FlowTaskStatus> tmp = new();
                var response = JObject.Parse(responseStr);
                var swarmDatas = (JArray)response["swarm_data"];
                foreach (var swarmData in swarmDatas)
                {
                    string flowID = (string)swarmData["flow_id"];
                    var tasks = (JArray)swarmData["tasks"];
                    //only one task
                    var taskParameter = tasks[0];
                    //foreach (var taskParameter in tasks)
                    //{
                    string amrid = (string)taskParameter["robot_id"];
                    int state = (int)taskParameter["state"];
                    string stateMsg = (string)taskParameter["status_msg"];
                    int percentage = (int)taskParameter["complete_percent"];
                    var custom_msg = taskParameter["customized_info"];
                    string report = (string)custom_msg["Report"];
                    FlowTaskStatus flowTaskStatus = new FlowTaskStatus { flowid = flowID, amrid = amrid, state = state, status_msg = stateMsg, percentage = percentage, custom_info = report };
                    OnSingleFlowTaskStatus(flowTaskStatus);
                    tmp.Add(flowTaskStatus);
                    //}

                }
                swarmCoreTaskStatus = tmp;
            }
            catch (Exception e)
            {
                Console.WriteLine("update swarm core task fail  at " + baseURL + ":" + postfix);
                await UpdateToken();
                Console.WriteLine("retry get token by task status");
            }
        }

        public (bool, string) GetMZYByMesNo(string mesNo)
        {
            bool res = false;
            string resStr = "unknow";
            MesTaskDetail targetTask = MesTasks_WIP.FirstOrDefault(x => x.TaskNoFromMes == mesNo);
            if (targetTask == null)
            {
                res = false;
                resStr = mesNo + " is not in WIP";
            }
            else
            {
                string flowid = targetTask.TaskNoFromSwarmCore;
                FlowTaskStatus flowStatus = swarmCoreTaskStatus.FirstOrDefault(x => x.flowid == flowid);
                if (flowStatus == null)
                {
                    res = false;
                    resStr = mesNo + " task status not found";
                }
                else
                {
                    List<string> noMZYStep = new List<string> { "moving1", "docking1", "handshake1", "moving3" };
                    List<string> withMZYStep = new List<string> { "barcodecheck1", "moving2", "docking2", "barcodecheck2", "handshake2" };
                    if (noMZYStep.Contains(flowStatus.custom_info))
                    {
                        res = false;
                        resStr = "null";
                    }
                    if (withMZYStep.Contains(flowStatus.custom_info))
                    {
                        if (targetTask.Status != 3)
                        {
                            res = true;
                            resStr = targetTask.Barcode;
                        }
                        //error
                        else
                        {
                            if (flowStatus.status_msg.Contains("barcode not matched"))
                            {
                                res = true;
                                resStr = flowStatus.status_msg.Split(':').Last().Trim();
                            }
                            else
                            {
                                res = true;
                                resStr = targetTask.Barcode;
                            }
                        }
                    }
                }
            }
            return (res, resStr);
        }

        public event Action<FlowTaskStatus>? SingleFlowTaskStatusChangeAct;
        private void OnSingleFlowTaskStatus(FlowTaskStatus flowTaskStatus) => SingleFlowTaskStatusChangeAct?.Invoke(flowTaskStatus);
        public enum TaskStatus
        {
            Queue = 0, Active = 1, Complete = 2, Fail = 3, Pause = 4, Cancel = 5, Unknow = 6,
        }

        private List<AMRStatus> FARobotToAMRStatus(FleetState fleet, DateTime updateTime)
        {
            List<AMRStatus> res = new();
            if (fleet.robots == null)
            {
                return res;
            }
            foreach (Robot robot in fleet.robots)
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
                        else
                        {
                            try
                            {
                                await plcClass.SyncPLCModbus(master);
                            }
                            catch { }

                        }
                    }
                    OnSinglePLCClassChange(plcClass);
                });
                await Task.Delay(1);
            });
            OnPLCClassesChange();
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
            OnPLCClassesChange();
        }

        public IEnumerable<MxmodbusIndex> GetPLCIndexTable(string type)
        {
            return indexTable;
        }

        public IEnumerable<PLCClass> GetPLCClasses()
        {
            return plcClasses;
        }
        public void ResetPLCClass()
        {
            plcClasses = new List<PLCClass>();
        }
        private async Task ToPLCClasses(IEnumerable<Plcconfig> plcconfigs)
        {
            plcClasses = new();
            Log.Information("config " + plcconfigs.Count());
            //await Task.Run(async () =>
            //{
            //Parallel.ForEach(plcconfigs, async plcconfig =>
            //{
            List<Task> initTask = new();
            //int count1 = 0;
            //int count2 = 0;
            foreach (Plcconfig plcconfig in plcconfigs)
            {
                initTask.Add(Task.Run(async () =>
                {
                    List<MxmodbusIndex> typeIndexTable = indexTable.Where(x => x.Plctype == plcconfig.Plctype).ToList();
                    PLCClass tmp = new PLCClass(plcconfig, typeIndexTable, plcRetryTimes);
                    //if (tmp.keepUpdate)
                    //{
                    //    await tmp.TryConnectTcp();
                    //}
                    plcClasses.Add(tmp);
                    //OnSinglePLCClassChange(tmp);
                }));
            }
            //OnPLCClassesChange();
            //});
            await Task.WhenAll(initTask);
            List<Task> connectTask = new();
            foreach (PLCClass plc in plcClasses)
            {
                connectTask.Add(Task.Run(async () =>
                {
                    if (plc.keepUpdate)
                    {
                        await plc.TryConnectTcp();
                    }
                    //plcClasses.Add(plc);
                    //OnSinglePLCClassChange(plc);
                }));
            }
            await Task.WhenAll(connectTask);
            //Log.Information("init " + plcClasses.Count());
            //await Task.Delay(100);
            //});
            OnPLCClassesChange();
        }

        public async Task ManuallyAddPLC(Plcconfig config)
        {
            List<MxmodbusIndex> typeIndexTable = indexTable.Where(x => x.Plctype == config.Plctype).ToList();
            PLCClass tmp = new PLCClass(config, typeIndexTable, plcRetryTimes);
            //if (tmp.keepUpdate)
            //{
            //    await tmp.TryConnectTcp();
            //}
            tmp.keepUpdate = false;
            tmp.selfCheckFlag = false;
            plcClasses.Add(tmp);
            OnSinglePLCClassChange(tmp);
        }

        public event Action<PLCClass>? SinglePLCClassChangeAct;
        private void OnSinglePLCClassChange(PLCClass plcClass) => SinglePLCClassChangeAct?.Invoke(plcClass);

        public event Action<List<PLCClass>>? PLCClassesChangeAct;
        private void OnPLCClassesChange() => PLCClassesChangeAct?.Invoke(plcClasses);
        #endregion



        #region Mes
        public List<MesTaskDetail> GetAllTasks()
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AGVDBContext>();
                return context.MesTaskDetails.ToList();
            }
        }
        public List<MesTaskDetail> GetWIPTasks()
        {
            return MesTasks_WIP.ToList();
        }
        public List<MesTaskDetail> GetWIPTasksByNO(string no)
        {
            return MesTasks_WIP.Where(x => x.TaskNoFromMes == no).ToList();
        }

        public List<MesTaskDetail> GetFinishedTask()
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AGVDBContext>();

                return context.MesTaskDetails.Where(x => x.Status == 2).ToList();
            }
        }
        public bool GetFinishedTaskExist(string no)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AGVDBContext>();

                return context.MesTaskDetails.Any(x => x.Status == 2 && x.TaskNoFromMes == no);
            }
        }

        public async Task UpsertMesTaskForDB(MesTaskDetail mesTask)//for DB
        {
            await Task.Run(async () =>
            {
                using (var scope = scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AGVDBContext>();
                    bool taskExist = context.MesTaskDetails.Any(x => x.TaskNoFromMes == mesTask.TaskNoFromMes);
                    if (taskExist)
                    {
                        MesTaskDetail target = context.MesTaskDetails.FirstOrDefault(x => x.TaskNoFromMes == mesTask.TaskNoFromMes);
                        target.TaskNoFromSwarmCore = mesTask.TaskNoFromSwarmCore;
                        target.Amrid = mesTask.Amrid;
                        target.Status = mesTask.Status;
                        target.AssignToSwarmCoreTime = mesTask.AssignToSwarmCoreTime;
                        target.SwarmCoreActualStratTime = mesTask.SwarmCoreActualStratTime;
                        target.FailTime = mesTask.FailTime;
                        target.FinishOrTimeoutTime = mesTask.FinishOrTimeoutTime;
                        target.FinishReason = mesTask.FinishReason;
                    }
                    else
                    {
                        await context.MesTaskDetails.AddAsync(mesTask);
                    }
                    await context.SaveChangesAsync();
                }
            });
        }

        public async Task<(bool, string)> DeleteTaskFromSwarmCore(MesTaskDetail mesTask)
        {
            string postfix = baseURL + "/v2/flows/" + mesTask.TaskNoFromSwarmCore;
            var res = await httpClient_swarmCore.DeleteAsync(postfix);
            var respond = await res.Content.ReadAsStringAsync();
            try
            {
                var response = JObject.Parse(respond);
                int statusCode = (int)response["system_status_code"];
                string msg = (string)response["system_message"];
                if ((statusCode == 5550000 || statusCode == 200))
                {
                    return (true, "Remove Task " + mesTask.TaskNoFromMes + " success");
                }
                else
                {
                    return (false, "Remove Task " + mesTask.TaskNoFromMes + " fail (" + msg + ")");
                }
            }
            catch (Exception e)
            {
                return (false, "Remove Task " + mesTask.TaskNoFromMes + " fail (" + e.ToString() + ")");
            }
        }

        public async Task<(bool, string)> PauseTaskFromSwarmCore(MesTaskDetail mesTask)
        {
            string postfix = baseURL + "/v2/flows/" + mesTask.TaskNoFromSwarmCore + "?action=pause";
            var res = await httpClient_swarmCore.PutAsync(postfix, null);
            var respond = await res.Content.ReadAsStringAsync();
            try
            {
                var response = JObject.Parse(respond);
                int statusCode = (int)response["system_status_code"];
                string msg = (string)response["system_message"];
                if ((statusCode == 5550000 || statusCode == 200))
                {
                    return (true, "Pause Task " + mesTask.TaskNoFromMes + " success");
                }
                else
                {
                    return (false, "Pause Task " + mesTask.TaskNoFromMes + " fail (" + msg + ")");
                }
            }
            catch (Exception e)
            {
                return (false, "Pause Task " + mesTask.TaskNoFromMes + " fail (" + e.ToString() + ")");
            }
        }

        public async Task<(bool, string)> ResumeTaskFromSwarmCore(MesTaskDetail mesTask)
        {
            string postfix = baseURL + "/v2/flows/" + mesTask.TaskNoFromSwarmCore + "?action=resume";
            var res = await httpClient_swarmCore.PutAsync(postfix, null);
            var respond = await res.Content.ReadAsStringAsync();
            try
            {
                var response = JObject.Parse(respond);
                int statusCode = (int)response["system_status_code"];
                string msg = (string)response["system_message"];
                if ((statusCode == 5550000 || statusCode == 200))
                {
                    return (true, "Resume Task " + mesTask.TaskNoFromMes + " success");
                }
                else
                {
                    return (false, "Resume Task " + mesTask.TaskNoFromMes + " fail (" + msg + ")");
                }
            }
            catch (Exception e)
            {
                return (false, "Resume Task " + mesTask.TaskNoFromMes + " fail (" + e.ToString() + ")");
            }
        }

        public async Task DeleteMesTaskForWIP(MesTaskDetail mesTask)//status, amr id and all time logs
        {
            await Task.Run(() =>
            {
                bool taskExist = MesTasks_WIP.Any(x => x.TaskNoFromMes == mesTask.TaskNoFromMes);
                if (taskExist)
                {
                    MesTasks_WIP.Remove(mesTask);
                }
            });
        }
        public MesTaskDetail InitMesTask(ImesTask mesTask, string t)
        {
            MesTaskDetail res = new MesTaskDetail
            {
                TaskNoFromMes = mesTask.TaskNoFromMes,
                TaskType = mesTask.TaskType,
                FromStation = mesTask.FromStation,
                ToStation = mesTask.ToStation,
                Barcode = mesTask.Barcode,
                LoaderToAmrhighOrLow = mesTask.LoaderToAmrhighOrLow,
                AmrtoLoaderHighOrLow = mesTask.AmrtoLoaderHighOrLow,

                Status = 0,
                GetFromMesTime = t,
                Amrid = "not assign",
                TaskNoFromSwarmCore = "unknown",
                AssignToSwarmCoreTime = "not yet",
                SwarmCoreActualStratTime = "not yet",
                FailTime = "not yet",
                FinishOrTimeoutTime = "not yet",
                FinishReason = "unknown",
            };
            Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized");
            return res;
        }
        public MesTaskDetail InitMesTask(ReviseTask reviseTask, string t)
        {
            MesTaskDetail res = new MesTaskDetail
            {
                TaskNoFromMes = reviseTask.NewTaskMesNo,
                TaskType = 3,
                FromStation = "null",
                ToStation = reviseTask.ToStation,
                Barcode = reviseTask.Barcode,
                LoaderToAmrhighOrLow = false,
                AmrtoLoaderHighOrLow = reviseTask.AmrtoLoaderHighOrLow,

                Status = 0,
                GetFromMesTime = t,
                Amrid = "not assign",
                TaskNoFromSwarmCore = "unknown",
                AssignToSwarmCoreTime = "not yet",
                SwarmCoreActualStratTime = "not yet",
                FailTime = "not yet",
                FinishOrTimeoutTime = "not yet",
                FinishReason = "unknown",
            };
            Log.Information("mesTask " + reviseTask.NewTaskMesNo + " is initilized");
            return res;
        }
        public void AssignMesToSwarmCore(MesTaskDetail mesTask, string swarmcoreTaskNo)
        {
            mesTask.TaskNoFromSwarmCore = swarmcoreTaskNo;
            mesTask.AssignToSwarmCoreTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            Log.Information("mesTask " + mesTask.TaskNoFromMes + " is assigned to Swarmcore");
        }
        public async Task SwarmCoreStartProcessing(MesTaskDetail mesTask, string amr)
        {
            mesTask.Status = 1;
            mesTask.Amrid = amr;
            mesTask.SwarmCoreActualStratTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            await UpsertMesTaskForDB(mesTask);
            Log.Information("mesTask " + mesTask.TaskNoFromMes + " start running and update to DB");
        }
        public async Task SwarmCoreUpdateStatusFailPauseWithTimeLog(MesTaskDetail mesTask, int status)
        {
            mesTask.Status = status;
            mesTask.FailTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            await UpsertMesTaskForDB(mesTask);
        }

        public async Task<(bool, string)> CancelWIPTask(string tasknoFromMes/*, string reason*/)
        {
            await UpdateSwarmCoreTaskStatus();
            if (!MesTasks_WIP.Exists(x => x.TaskNoFromMes == tasknoFromMes))
            {
                return (false, tasknoFromMes + " not exist in WIP");
            }
            else
            {
                MesTaskDetail target = MesTasks_WIP.FirstOrDefault(x => x.TaskNoFromMes == tasknoFromMes);
                if (target == null)
                {
                    return (false, "no task " + tasknoFromMes);
                }
                FlowTaskStatus targetFlow = GetSwarmCoreTaskStatus().FirstOrDefault(x => x.flowid == target.TaskNoFromSwarmCore);
                if (targetFlow == null)
                {
                    return (false, "no task status of " + tasknoFromMes);
                }
                else
                {
                    //queue active, fail or pause
                    if (target.Status == 0)
                    {
                        (bool, string) res = await DeleteTaskFromSwarmCore(target);
                        if (res.Item1)
                        {
                            string reason = "queue";
                            await RemoveFromWIP(target, reason);
                            MesTasks_WIP.Remove(target);
                            OnMesTaskCancel(target);
                            return (true, reason);
                        }
                        else if (!res.Item1)
                        {
                            return (false, res.Item2);
                        }

                    }
                    //active & pause
                    else if (target.Status == 1 || target.Status == 4 || target.Status == 3)
                    {
                        (bool, string) res = await DeleteTaskFromSwarmCore(target);
                        if (res.Item1)
                        {
                            string reason = (DataBufferService.TaskStatus)target.Status+"";
                            reason += ("-" + targetFlow.custom_info);
                            reason += ("("+ GetTaskErrorCode(tasknoFromMes) + ")");
                            string msg = "";
                            switch (targetFlow.custom_info)
                            {
                                case "moving1":
                                case "docking1":
                                case "moving3":
                                    msg = "please check AMR";
                                    break;
                                case "handshake1":
                                case "handshake2":
                                    msg = "please reset AMR, loader and remove magazine from AMR";
                                    break;
                                case "barcodecheck1":
                                case "moving2":
                                case "docking2":
                                case "barcodecheck2":
                                    msg = "please remove magazine from AMR";
                                    break;
                                default:
                                    break;
                            }
                            await RemoveFromWIP(target, reason);
                            MesTasks_WIP.Remove(target);
                            OnMesTaskCancel(target);
                            return (true, reason+", "+msg);
                        }
                        else if (!res.Item1)
                        {
                            return (false, res.Item2);
                        }
                        

                    }
                    //fail
                    //else if (target.Status == 3)
                    //{

                    //}
                    else
                    {
                        return (false, tasknoFromMes + " is " + target.Status + ", not allow to cancel");
                    }
                }
                return (false, tasknoFromMes + " is " + target.Status + ", not allow to cancel");
            }
        }

        public event Action<MesTaskDetail>? MesTaskCancelAct;
        private void OnMesTaskCancel(MesTaskDetail mesTask) => MesTaskCancelAct?.Invoke(mesTask);

        public async Task<(bool, string)> GetNewMESTask(ImesTask iMesTask)
        {
            if (iMesTask.TaskNoFromMes.Contains("BOT") || iMesTask.TaskNoFromMes.Contains("TOP") || iMesTask.TaskNoFromMes.Contains("selftest"))
            {
                iMesTask.TaskNoFromMes += ("_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            }
            Log.Information("get mesTask " + iMesTask.TaskNoFromMes);
            string t_receive = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            //if (!CheckFromCanAssign(iMesTask) || !CheckToCanAssign(iMesTask))
            //{
            //	//RefreshGroupFlag(iMesTask, true);
            //	queueForGroup.Add(iMesTask);
            //	OnQueueTaskChange();
            //	return (true, "Group occupied queue the task in group");
            //}
            //else
            //{
            //	RefreshGroupFlag(iMesTask, true);
            //}
            //auto assign task no. with time
            //if (iMesTask.TaskNoFromMes.Contains("test"))
            //{
            //	iMesTask.TaskNoFromMes = "test_" + DateTime.Now.ToString("yyyyMMddHHmmss");
            //	MesTaskDetail mesTask = InitMesTask(iMesTask);
            //	await UpsertMesTaskForDB(mesTask);//only insert here
            //	Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
            //	MesTasks_WIP.Add(mesTask);
            //	Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
            //	OnSingleMesTaskChange(mesTask);
            //	//RefreshGroupFlag(iMesTask, false);
            //	return (true, "testing assign success");
            //}
            if (iMesTask.Barcode.Count() != 12)
            {
                string info = "mesTask " + iMesTask.Barcode + " is invalid";
                Log.Warning(info);
                return (false, info);
            }
            if (MesTasks_WIP.Exists(x => x.TaskNoFromMes == iMesTask.TaskNoFromMes))
            {
                string info = "mesTask " + iMesTask.TaskNoFromMes + " from api already exist in WIP so drop it";
                Log.Warning(info);
                return (false, info);
            }
            if (GetFinishedTaskExist(iMesTask.TaskNoFromMes))
            {
                string info = "mesTask " + iMesTask.TaskNoFromMes + " from api already exist in DB so drop it";
                Log.Warning(info);
                return (false, info);
            }
            bool returnFlag = false;
            string returnStr = "fali before assigning task " + iMesTask.TaskNoFromMes;
            string postfix = "/v2/flows";
            switch (iMesTask.TaskType)
            {
                //auto to auto
                case 0:
                    {
                        if (iMesTask.FromStation.Contains("STCL") || iMesTask.ToStation.Contains("STCU"))
                        {
                            return (false, "can't start from STCL or to STCU");
                        }
                        //get plc class by from & to
                        PLCClass start = plcClasses.FirstOrDefault(x => x.name.Contains(iMesTask.FromStation) && x.tcpConnect);
                        PLCClass destination = plcClasses.FirstOrDefault(x => x.name.Contains(iMesTask.ToStation) && x.tcpConnect);
                        if (start == null || destination == null)
                        {
                            return (false, "check loader station exist and connected");
                        }
                        Plcconfig start_config = plcconfigs.FirstOrDefault(x => x.Name.Contains(iMesTask.FromStation));
                        Plcconfig dest_config = plcconfigs.FirstOrDefault(x => x.Name.Contains(iMesTask.ToStation));
                        if (start_config == null || dest_config == null)
                        {
                            return (false, "check plc config");
                        }
                        //check station from to validation

                        //point parameter
                        string startPointStr = iMesTask.LoaderToAmrhighOrLow ? iMesTask.FromStation.ToString() + "_up" : iMesTask.FromStation.ToString() + "_down";
                        string endPointStr = iMesTask.AmrtoLoaderHighOrLow ? iMesTask.ToStation.ToString() + "_up" : iMesTask.ToStation.ToString() + "_down";

                        CellConfig startCellConfig = cellConfigs.FirstOrDefault(x => x.CellName == startPointStr);
                        CellConfig destCellConfig = cellConfigs.FirstOrDefault(x => x.CellName == endPointStr);
                        if (startCellConfig == null || endPointStr == null)
                        {
                            return (false, "check cell configs of " + startPointStr + " & " + endPointStr);
                        }
                        string startLeavePoint = startCellConfig.LeaveCell;
                        string endLeavePoint = destCellConfig.LeaveCell;

                        //bool sameArea = startCellConfig.ArtifactId == destCellConfig.ArtifactId ? true : false;

                        //point parameter
                        //string startGateInCell = startCellConfig.GateInCell;
                        //string startGateInArea = startGateInCell.Contains("gate") ? "gate" : "default_area";
                        //string startArtifactID = startCellConfig.ArtifactId;
                        //string startInOperation = startArtifactID == "artifact_39578" ? "pass" : "enter";
                        //string startOutOperation = startArtifactID == "artifact_39578" ? "pass" : "leave";

                        //string startRotateCell = startCellConfig.RotateCell;
                        //string startRotateDegree = startCellConfig.RotateDegree;
                        //string startRotateTarget = startCellConfig.RotateDest;
                        string startGateOutCell = startCellConfig.LeaveCell;

                        //string startGateOutArea = startGateOutCell.Contains("gate") ? "gate" : "default_area";

                        //destination point parameter

                        //string destGateInCell = destCellConfig.GateInCell;

                        //string destGateInArea = destGateInCell.Contains("gate") ? "gate" : "default_area";

                        //string destArtifactID = destCellConfig.ArtifactId;
                        //string destInOperation = destArtifactID == "artifact_39578" ? "pass" : "enter";

                        //string destOutOperation = destArtifactID == "artifact_39578" ? "pass" : "leave";

                        //string destRotateCell = destCellConfig.RotateCell;
                        //string destRotateDegree = destCellConfig.RotateDegree;
                        //string destRotateTarget = destCellConfig.RotateDest;

                        string destGateOutCell = destCellConfig.LeaveCell;

                        //string destGateOutArea = destGateOutCell.Contains("gate") ? "gate" : "default_area";

                        int startBaseIndex = iMesTask.LoaderToAmrhighOrLow ? start.startIndex + 5 : start.startIndex + 5 + 9;
                        int destBaseIndex = iMesTask.AmrtoLoaderHighOrLow ? destination.startIndex : destination.startIndex + 9;

                        int x_start = 2;
                        int x_dest = 1;

                        int y_start = startCellConfig.AlignSide ? 2 : 1;
                        int y_dest = destCellConfig.AlignSide ? 2 : 1;

                        int z_start = iMesTask.LoaderToAmrhighOrLow ? 1 : 2;
                        string startPostfix = iMesTask.LoaderToAmrhighOrLow ? "_up" : "_down";

                        int z_dest = iMesTask.AmrtoLoaderHighOrLow ? 1 : 2;
                        string destPostfix = iMesTask.AmrtoLoaderHighOrLow ? "_up" : "_down";

                        int cmd_start = x_start * 256 + y_start * 16 + z_start;
                        int cmd_dest = x_dest * 256 + y_dest * 16 + z_dest;

                        int stationNO_start = start.name.Contains("STCU") ? start.no : start.no + 4;
                        int stationNO_dest = destination.no;

                        //if (startCellConfig.ArtifactId == destCellConfig.ArtifactId && startCellConfig.ArtifactId != "artifact_39578")
                        //{
                        //    startOutOperation = "stay";
                        //    destInOperation = "stay";

                        //    startGateOutCell = startCellConfig.CellName;
                        //    //startGateOutArea = "default_area";

                        //    destGateInCell = destCellConfig.RotateCell;
                        //    //destGateOutArea = "default_area";
                        //}
                        //string startGateOutArea = startGateOutCell.Contains("gate") ? "gate" : "default_area";
                        //string destGateInArea = destGateInCell.Contains("gate") ? "gate" : "default_area";


                        var args = new
                        {
                            start_time = "",
                            end_time = "",
                            interval = "",
                            @params = new
                            {
                                global = new
                                {
                                    //write barcode
                                    value_1RJzO = "cmd:setBarcode,targetval:" + iMesTask.Barcode,

                                    //first handshake
                                    //move, dockingArtifact
                                    goal_neRCo = "ADLINK_Final_1" + "@default_area@" + startPointStr,

                                    //artifact_id_tUxuD = startArtifactID,
                                    //value_tUxuD = "operation:" + startInOperation,

                                    //goal_RgcuQ = "ADLINK_Final_1" + "@default_area@" + startRotateCell,

                                    //angle_8zvcZ = startRotateDegree,
                                    //goal_8zvcZ = "ADLINK_Final_1" + "@default_area@" + startRotateTarget,

                                    //goal_BYw8v = "ADLINK_Final_1" + "@default_area@" + startPointStr,

                                    value_dMBsM = "cmd:m92_get,targetval:true",
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

                                    //leave
                                    goal_gfxRJ = "ADLINK_Final_1" + "@default_area@" + startLeavePoint,

                                    //artifact_id_zH5Z6 = startArtifactID,
                                    //value_zH5Z6 = "operation:" + startOutOperation,



                                    //second handshake
                                    //move, dockingArtifact
                                    //goal_NST2k = "ADLINK_Final_1" + "@" + destGateInArea + "@" + destGateInCell,

                                    //artifact_id_8Nxs7 = destArtifactID,
                                    //value_8Nxs7 = "operation:" + destInOperation,

                                    //goal_gT5bw = "ADLINK_Final_1" + "@default_area@" + destRotateCell,

                                    //goal_6IlCp = "ADLINK_Final_1" + "@default_area@" + destRotateTarget,
                                    //angle_6IlCp = destRotateDegree,

                                    goal_mtxL2 = "ADLINK_Final_1" + "@default_area@" + endPointStr,

                                    value_8S55v = "cmd:m92_get,targetval:true",
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

                                    //leave
                                    goal_XKQwi = "ADLINK_Final_1" + "@default_area@" + endLeavePoint,

                                    //artifact_id_NuRx0 = destArtifactID,
                                    //value_NuRx0 = "operation:" + destOutOperation,

                                    //assigned_robot = "smr_9901010201002t73igf1",
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
                        //if (!iMesTask.TaskNoFromMes.Trim().Contains("test"))
                        //{
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

                                MesTaskDetail mesTask = InitMesTask(iMesTask, t_receive);
                                Console.WriteLine(flowID);
                                AssignMesToSwarmCore(mesTask, flowID);
                                await UpsertMesTaskForDB(mesTask);//only insert here
                                Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
                                MesTasks_WIP.Add(mesTask);
                                Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
                                OnSingleMesTaskChange(mesTask);
                                //RefreshGroupFlag(iMesTask, true);
                                return (true, "assign success");

                            }
                            else
                            {
                                Log.Information("assign mesTask " + iMesTask.TaskNoFromMes + " fail");
                                return (false, "error occured when assigning to swarmcore (" + msg + ")");
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Information("assign mesTask " + iMesTask.TaskNoFromMes + " fail");
                            return (false, "error occured when assigning to swarmcore");
                        }
                        //}
                    }
                    break;
                //manual to auto
                case 1:
                    {
                        if (iMesTask.FromStation.Contains("STCL") || iMesTask.ToStation.Contains("STCU"))
                        {
                            return (false, "can't start from STCL or to STCU");
                        }
                        //get plc class by from & to
                        ManualStationConfig start_config = manualStationConfigs.FirstOrDefault(x => x.Name.Contains(iMesTask.FromStation));
                        if (start_config == null)
                        {
                            return (false, "check manual station config");
                        }
                        Plcconfig dest_config = plcconfigs.FirstOrDefault(x => x.Name.Contains(iMesTask.ToStation));
                        if (dest_config == null)
                        {
                            return (false, "check plc config");
                        }

                        PLCClass destination = plcClasses.FirstOrDefault(x => x.name.Contains(iMesTask.ToStation) && x.tcpConnect);
                        if (destination == null)
                        {
                            return (false, "check loader station exist and connected");
                        }
                        string endPointStr = iMesTask.AmrtoLoaderHighOrLow ? iMesTask.ToStation.ToString() + "_up" : iMesTask.ToStation.ToString() + "_down";
                        CellConfig destCellConfig = cellConfigs.FirstOrDefault(x => x.CellName == endPointStr);
                        if (destCellConfig == null)
                        {
                            return (false, "check cell configs of " + endPointStr);
                        }

                        //bool sameArea = start_config.ArtifactId == dest_config.ArtifactId ? true : false;
                        //manual start station
                        string startPointStr = iMesTask.FromStation;

                        string startGateInCell = start_config.GateInCell;

                        string startGateInArea = startGateInCell.Contains("gate") ? "gate" : "default_area";

                        string startArtifactID = start_config.ArtifactId;
                        string startInOperation = startArtifactID == "artifact_39578" ? "pass" : "enter";
                        string startOutOperation = startArtifactID == "artifact_39578" ? "pass" : "leave";

                        string startRotateCell = start_config.RotateCell;
                        string startRotateDegree = start_config.RotateDegree;
                        string startRotateTarget = start_config.RotateDest;

                        string startGateOutCell = start_config.GateOutCell;
                        string startGateOutArea = startGateOutCell.Contains("gate") ? "gate" : "default_area";

                        //string areaTemporary = sameArea ? "default_area" : "gate";


                        //auto destination station

                        //string destGateInCell = destCellConfig.GateInCell;


                        //string destArtifactID = destCellConfig.ArtifactId;
                        //string destInOperation = destArtifactID == "artifact_39578" ? "pass" : "enter";
                        //string destOutOperation = destArtifactID == "artifact_39578" ? "pass" : "leave";

                        //string destRotateCell = destCellConfig.RotateCell;
                        //string destRotateDegree = destCellConfig.RotateDegree;
                        //string destRotateTarget = destCellConfig.RotateDest;

                        string destGateOutCell = destCellConfig.LeaveCell;
                        //string destGateOutArea = destGateOutCell.Contains("gate") ? "gate" : "default_area";


                        //dest parameter
                        int destBaseIndex = iMesTask.AmrtoLoaderHighOrLow ? destination.startIndex : destination.startIndex + 9;

                        int x_dest = 1;

                        int y_dest = destCellConfig.AlignSide ? 2 : 1;
                        //if (destination.name.Contains("STCL"))
                        //{
                        //	y_dest = 2;
                        //}

                        int z_dest = iMesTask.AmrtoLoaderHighOrLow ? 1 : 2;
                        string destPostfix = iMesTask.AmrtoLoaderHighOrLow ? "_up" : "_down";

                        int cmd_dest = x_dest * 256 + y_dest * 16 + z_dest;

                        int stationNO_dest = destination.no;

                        //if (start_config.ArtifactId == destCellConfig.ArtifactId && start_config.ArtifactId != "artifact_39578")
                        //{
                        //    startOutOperation = "stay";
                        //    destInOperation = "stay";
                        //}

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
                                    value_rTj5w = "cmd:setBarcode,targetval:" + iMesTask.Barcode,

                                    //first handshake
                                    //enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
                                    //goal_akw4l = "ADLINK_Final_1" + "@" + startGateInArea + "@" + startGateInCell,

                                    //artifact_id_i6J0T = startArtifactID,
                                    //value_i6J0T = "operation:" + startInOperation,

                                    //goal_UX8sH = "ADLINK_Final_1" + "@default_area@" + startRotateCell,

                                    //angle_msOdA = startRotateDegree,
                                    //goal_msOdA = "ADLINK_Final_1" + "@default_area@" + startRotateTarget,

                                    goal_hSU2V = "ADLINK_Final_1" + "@default_area@" + startPointStr,

                                    value_FbWUK = "cmd:m92_get,targetval:true",
                                    goal_FbWUK = "ADLINK_Final_1" + "@default_area@" + startPointStr,

                                    //set station
                                    value_yi1qe = "cmd:d305_set,targetval:" + start_config.No.ToString(),

                                    //leave gate
                                    goal_ys26n = "ADLINK_Final_1" + "@default_area@" + startGateOutCell,

                                    //artifact_id_mbuRS = startArtifactID,
                                    //value_mbuRS = "operation:" + startOutOperation,



                                    //handshake
                                    //enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
                                    //goal_SjyCS = "ADLINK_Final_1" + "@" + destGateOutArea + "@" + destGateInCell,

                                    //artifact_id_5q4C4 = destArtifactID,
                                    //value_5q4C4 = "operation:" + destInOperation,

                                    //goal_VBozh = "ADLINK_Final_1" + "@default_area@" + destRotateCell,

                                    //goal_UcrvG = "ADLINK_Final_1" + "@default_area@" + destRotateTarget,
                                    //angle_UcrvG = destRotateDegree,

                                    goal_KH3a0 = "ADLINK_Final_1" + "@default_area@" + endPointStr,

                                    value_JKiPz = "cmd:m92_get,targetval:true",
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
                                    goal_R4APq = "ADLINK_Final_1" + "@default_area@" + destGateOutCell,

                                    //artifact_id_iHk6v = destArtifactID,
                                    //value_iHk6v = "operation:" + destOutOperation,

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
                        //if (!iMesTask.TaskNoFromMes.Trim().Contains("test"))
                        //{
                        var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/manual_auto_flow", new StringContent(body, Encoding.UTF8, "application/json"));
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

                                MesTaskDetail mesTask = InitMesTask(iMesTask, t_receive);
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
                                Log.Information("assign mesTask " + iMesTask.TaskNoFromMes + " fail");
                                return (false, "error occured when assigning to swarmcore (" + msg + ")");
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Information("assign mesTask " + iMesTask.TaskNoFromMes + " fail");
                            return (false, "error occured when assigning to swarmcore");
                        }
                        //}
                        //test data
                        //else
                        //{
                        //	MesTaskDetail mesTask = InitMesTask(iMesTask);
                        //	await UpsertMesTaskForDB(mesTask);//only insert here
                        //	Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
                        //	MesTasks_WIP.Add(mesTask);
                        //	Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
                        //	OnSingleMesTaskChange(mesTask);
                        //	return (true, "testing assign success");
                        //}
                    }
                    break;
                //auto to manual
                case 2:
                    {
                        //get plc class by from & to
                        PLCClass start = plcClasses.FirstOrDefault(x => x.name.Contains(iMesTask.FromStation) && x.tcpConnect);
                        if (start == null)
                        {
                            return (false, "check loader " + iMesTask.FromStation + " exist and connected");
                        }
                        Plcconfig start_config = plcconfigs.FirstOrDefault(x => x.Name.Contains(iMesTask.FromStation));
                        if (start_config == null)
                        {
                            return (false, "check plc config");
                        }
                        string startPointStr = iMesTask.LoaderToAmrhighOrLow ? iMesTask.FromStation.ToString() + "_up" : iMesTask.FromStation.ToString() + "_down";
                        CellConfig startCellConfig = cellConfigs.FirstOrDefault(x => x.CellName == startPointStr);
                        if (startCellConfig == null)
                        {
                            return (false, "check cell config of " + iMesTask.FromStation);
                        }
                        ManualStationConfig dest_config = manualStationConfigs.FirstOrDefault(x => x.Name.Contains(iMesTask.ToStation));
                        if (dest_config == null)
                        {
                            return (false, "check plc config");
                        }
                        if (start.name.Contains("STCL"))
                        {
                            return (false, "can't start from STCL");
                        }
                        //auto from station

                        //string startGateInCell = startCellConfig.GateInCell;
                        //string startArtifactID = startCellConfig.ArtifactId;
                        //string startInOperation = startArtifactID == "artifact_39578" ? "pass" : "enter";
                        //string startOutOperation = startArtifactID == "artifact_39578" ? "pass" : "leave";

                        //string startRotateCell = startCellConfig.RotateCell;
                        //string startRotateDegree = startCellConfig.RotateDegree;
                        //string startRotateTarget = startCellConfig.RotateDest;

                        string startGateOutCell = startCellConfig.LeaveCell;

                        //string startGateInArea = startGateInCell.Contains("gate") ? "gate" : "default_area";
                        //string startGateOutArea = startGateOutCell.Contains("gate") ? "gate" : "default_area";


                        //manual destination station
                        string endPointStr = iMesTask.ToStation;

                        //string destGateInCell = dest_config.GateInCell != "" ? dest_config.GateInCell : endPointStr;
                        //string destArtifactID = dest_config.ArtifactId;
                        //string destInOperation = destArtifactID == "artifact_39578" ? "pass" : "enter";
                        //string destOutOperation = destArtifactID == "artifact_39578" ? "pass" : "leave";

                        //string destRotateCell = dest_config.RotateCell != "" ? dest_config.RotateCell : endPointStr;
                        //string destRotateDegree = dest_config.RotateDegree;
                        //string destRotateTarget = dest_config.RotateDest;

                        //string destGateOutCell = dest_config.GateOutCell != "" ? dest_config.GateOutCell : endPointStr;

                        //string destGateInArea = destGateInCell.Contains("gate") ? "gate" : "default_area";
                        //string destGateOutArea = destGateOutCell.Contains("gate") ? "gate" : "default_area";

                        //start auto station parameter
                        int startBaseIndex = iMesTask.LoaderToAmrhighOrLow ? start.startIndex + 5 : start.startIndex + 5 + 9;

                        int x_start = 2;

                        int y_start = startCellConfig.AlignSide ? 2 : 1;

                        int z_start = iMesTask.LoaderToAmrhighOrLow ? 1 : 2;
                        string startPostfix = iMesTask.LoaderToAmrhighOrLow ? "_up" : "_down";

                        int cmd_start = x_start * 256 + y_start * 16 + z_start;

                        int stationNO_start = start.name.Contains("STCU") ? start.no : start.no + 4;

                        //if (startCellConfig.ArtifactId == dest_config.ArtifactId && dest_config.ArtifactId != "artifact_39578")
                        //{
                        //    startOutOperation = "stay";
                        //    destInOperation = "stay";
                        //}

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
                                    value_krG4K = "cmd:setBarcode,targetval:" + iMesTask.Barcode,

                                    //first handshake
                                    //enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
                                    goal_l50AK = "ADLINK_Final_1" + "@default_area@" + startPointStr,

                                    //artifact_id_JcTlC = startArtifactID,
                                    //value_JcTlC = "operation:" + startInOperation,

                                    //goal_uCX2t = "ADLINK_Final_1" + "@default_area@" + startRotateCell,

                                    //angle_mgYqR = startRotateDegree,
                                    //goal_mgYqR = "ADLINK_Final_1" + "@default_area@" + startRotateTarget,

                                    //goal_CDQ4v = "ADLINK_Final_1" + "@default_area@" + startPointStr,

                                    artifact_value = "cmd:m92_get,targetval:true",
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
                                    goal_Dp4xX = "ADLINK_Final_1" + "@default_area@" + startGateOutCell,

                                    //artifact_id_jAQf0 = startArtifactID,
                                    //value_jAQf0 = "operation:" + startOutOperation,



                                    //enter gate & docking (move, artifact, move, rotate, move, dockingArtifact)
                                    goal_NGivg = "ADLINK_Final_1" + "@default_area@" + endPointStr,

                                    //artifact_id_279vX = destArtifactID,
                                    //value_279vX = "operation:" + destInOperation,

                                    //goal_8GkDv = "ADLINK_Final_1" + "@default_area@" + destRotateCell,

                                    //goal_4sGL0 = "ADLINK_Final_1" + "@default_area@" + destRotateTarget,
                                    //angle_4sGL0 = destRotateDegree,

                                    //goal_NGivg = "ADLINK_Final_1" + "@default_area@" + endPointStr,

                                    value_HLlqM = "cmd:m92_get,targetval:true",
                                    goal_HLlqM = "ADLINK_Final_1" + "@default_area@" + endPointStr,

                                    //set station no
                                    value_27E0Y = "cmd:d305_set,targetval:" + dest_config.No.ToString(),

                                    //leave gate
                                    goal_xRCOG = "ADLINK_Final_1" + "@default_area@" + endPointStr,

                                    //artifact_id_zoI1l = destArtifactID,
                                    //value_zoI1l = "operation:" + destOutOperation,
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
                        //if (!iMesTask.TaskNoFromMes.Trim().Contains("test"))
                        //{
                        var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/auto_manual_flow", new StringContent(body, Encoding.UTF8, "application/json"));
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

                                MesTaskDetail mesTask = InitMesTask(iMesTask, t_receive);
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
                                Log.Information("assign mesTask " + iMesTask.TaskNoFromMes + " fail");
                                return (false, "error occured when assigning to swarmcore (" + msg + ")");
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Information("assign mesTask " + iMesTask.TaskNoFromMes + " fail");
                            return (false, "error occured when assigning to swarmcore");
                        }
                        //}
                        ////test data
                        //else
                        //{
                        //	MesTaskDetail mesTask = InitMesTask(iMesTask);
                        //	await UpsertMesTaskForDB(mesTask);//only insert here
                        //	Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
                        //	MesTasks_WIP.Add(mesTask);
                        //	Log.Information("testing mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
                        //	OnSingleMesTaskChange(mesTask);
                        //	return (true, "testing assign success");
                        //}
                    }
                default:
                    returnFlag = false;
                    returnStr = "there is only 0, 1 and 2 for tasktype";
                    break;
            }
            return (returnFlag, returnStr);
        }
        public string GetTaskErrorCode(string mesTaskNo)
        {
            //(swarmCoreTaskStatus.Exists(x => x.flowid == tmp.TaskNoFromSwarmCore && x.custom_info.Contains("barcodecheck") && x.status_msg.Contains("barcode not matched")))
            if (MesTasks_WIP.Exists(x => x.TaskNoFromMes == mesTaskNo))
            {
                MesTaskDetail tmp = MesTasks_WIP.FirstOrDefault(x => x.TaskNoFromMes == mesTaskNo);
                if (tmp.Status != 3)
                {
                    return "E000";
                }
                else
                {
                    if (swarmCoreTaskStatus.Exists(x => x.flowid == tmp.TaskNoFromSwarmCore))
                    {
                        string step = swarmCoreTaskStatus.FirstOrDefault(x => x.flowid == tmp.TaskNoFromSwarmCore).custom_info;
                        string custom_msg = swarmCoreTaskStatus.FirstOrDefault(x => x.flowid == tmp.TaskNoFromSwarmCore).status_msg;
                        switch (step)
                        {
                            case "moving1":
                                return "A001";
                            case "docking1":
                                return "A002";
                            case "handshake1":
                                return "A003";
                            case "barcodecheck1":
                                if (custom_msg.Contains("barcode not matched"))
                                {
                                    return "M001";
                                }
                                else
                                {
                                    return "A004";
                                }
                            case "moving2":
                                return "A005";
                            case "docking2":
                                return "A006";
                            case "barcodecheck2":
                                if (custom_msg.Contains("barcode not matched"))
                                {
                                    return "M002";
                                }
                                else
                                {
                                    return "A007";
                                }
                            case "handshake2":
                                return "A008";
                            case "moving3":
                                return "A009";
                            default:
                                return "E001";
                        }
                    }
                    else
                    {
                        return "E002";
                    }
                }
            }
            else
            {
                return "E002";
            }
        }
        public async Task<(bool, string)> GetReviseTask(ReviseTask reviseTask)
        {

            if (reviseTask.Barcode.Count() != 12)
            {
                string info = "mesTask " + reviseTask.Barcode + " is invalid";
                Log.Warning(info);
                return (false, info);
            }
            if (MesTasks_WIP.Exists(x => x.TaskNoFromMes == reviseTask.NewTaskMesNo))
            {
                string info = "mesTask " + reviseTask.NewTaskMesNo + " from api already exist in WIP so drop it";
                Log.Warning(info);
                return (false, info);
            }
            if (GetFinishedTaskExist(reviseTask.NewTaskMesNo))
            {
                string info = "mesTask " + reviseTask.NewTaskMesNo + " from api already exist in DB so drop it";
                Log.Warning(info);
                return (false, info);
            }
            Log.Information("get mesTask " + reviseTask.NewTaskMesNo);
            string t_receive = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

            MesTaskDetail originalTask = MesTasks_WIP.FirstOrDefault(x => x.TaskNoFromMes == reviseTask.OriginalMesTaskNo);
            if (originalTask == null)
            {
                return (false, "No fail WIP task: " + reviseTask.OriginalMesTaskNo);
            }

            bool returnFlag = false;
            string returnStr = "fali before assigning task " + reviseTask.NewTaskMesNo;
            string postfix = "/v2/flows";

            if (!reviseTask.ToStation.Contains("STCL") && reviseTask.ToStation != "XRCU03")
            {
                return (false, "Revise task only go to STCL");
            }
            //get plc class by from & to
            PLCClass destination = plcClasses.FirstOrDefault(x => x.name.Contains(reviseTask.ToStation) && x.tcpConnect);
            if (destination == null)
            {
                return (false, "check loader station exist and connected");
            }
            Plcconfig dest_config = plcconfigs.FirstOrDefault(x => x.Name.Contains(reviseTask.ToStation));
            if (dest_config == null)
            {
                return (false, "check plc config");
            }

            string endPointStr = reviseTask.AmrtoLoaderHighOrLow ? reviseTask.ToStation.ToString() + "_up" : reviseTask.ToStation.ToString() + "_down";
            CellConfig destCellConfig = cellConfigs.FirstOrDefault(x => x.CellName == endPointStr);
            if (endPointStr == null)
            {
                return (false, "check cell configs of " + endPointStr);
            }

            if (!swarmCoreTaskStatus.Exists(x => x.flowid == originalTask.TaskNoFromSwarmCore))
            {
                return (false, "no flow status of " + originalTask.TaskNoFromMes);
            }
            int destBaseIndex = reviseTask.AmrtoLoaderHighOrLow ? destination.startIndex : destination.startIndex + 9;
            string endLeavePoint = destCellConfig.LeaveCell;
            string errorLeaveCell = "";
            string flowStatus = swarmCoreTaskStatus.FirstOrDefault(x => x.flowid == originalTask.TaskNoFromSwarmCore).custom_info;
            if (flowStatus == "barcodecheck1")
            {
                string from = originalTask.FromStation;
                errorLeaveCell = originalTask.LoaderToAmrhighOrLow ? from + "_up" : from + "_down";

            }
            else if (flowStatus == "barcodecheck2")
            {
                string to = originalTask.ToStation;
                errorLeaveCell = originalTask.LoaderToAmrhighOrLow ? to + "_up" : to + "_down";
            }
            else
            {
                return (false, "can't get leave cell from error station");
            }
            CellConfig errorCell = cellConfigs.FirstOrDefault(x => x.CellName == errorLeaveCell);
            if (errorCell == null)
            {
                return (false, "check error cell configs of " + errorLeaveCell + " & " + endPointStr);
            }
            errorLeaveCell = errorCell.LeaveCell;

            int x_dest = 1;

            int y_dest = destCellConfig.AlignSide ? 2 : 1;
            int z_dest = reviseTask.AmrtoLoaderHighOrLow ? 1 : 2;
            string destPostfix = reviseTask.AmrtoLoaderHighOrLow ? "_up" : "_down";
            int cmd_dest = x_dest * 256 + y_dest * 16 + z_dest;
            int stationNO_dest = destination.no;

            string robot = originalTask.Amrid;

            var args = new
            {
                start_time = "",
                end_time = "",
                interval = "",
                @params = new
                {
                    global = new
                    {
                        //write barcode
                        value_2PeuI = "cmd:setBarcode,targetval:" + reviseTask.Barcode,

                        goal_ZUO3O = "ADLINK_Final_1" + "@default_area@" + errorLeaveCell,
                        goal_0FH3V = "ADLINK_Final_1" + "@default_area@" + endPointStr,

                        value_FmwMi = "cmd:m92_get,targetval:true",
                        goal_FmwMi = "ADLINK_Final_1" + "@default_area@" + endPointStr,

                        //loadin amr -> loader
                        value_QQOUj = "cmd:d305_set,targetval:" + destination.no.ToString(),
                        value_jbM1F = "cmd:d301_set,targetval:" + cmd_dest.ToString(),

                        value_aXZG7 = "cmd:m" + destBaseIndex.ToString() + "_set,targetval:true",
                        value_uGim5 = "cmd:m" + (destBaseIndex + 1).ToString() + "_get,targetval:true",
                        value_TP1RS = "cmd:m" + (destBaseIndex + 2).ToString() + "_set,targetval:true",
                        value_EWatA = "cmd:m" + (destBaseIndex + 3).ToString() + "_get,targetval:true",
                        //reset mc
                        value_whwPz = "cmd:m" + destBaseIndex.ToString() + "_set,targetval:false",
                        value_vAH6h = "cmd:m" + (destBaseIndex + 2).ToString() + "_set,targetval:false",

                        //leave
                        goal_A14Zi = "ADLINK_Final_1" + "@default_area@" + endLeavePoint,

                        //artifact_id_NuRx0 = destArtifactID,
                        //value_NuRx0 = "operation:" + destOutOperation,

                        assigned_robot = robot,
                    }
                }
            };
            var finalRes = new
            {
                args
            };
            Console.WriteLine(finalRes);
            var body = JsonConvert.SerializeObject(finalRes);

            var res = await httpClient_swarmCore.PostAsync(baseURL + postfix + "/revise_flow", new StringContent(body, Encoding.UTF8, "application/json"));
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

                    MesTaskDetail mesTask = InitMesTask(reviseTask, t_receive);
                    //Console.WriteLine(flowID);
                    AssignMesToSwarmCore(mesTask, flowID);
                    await UpsertMesTaskForDB(mesTask);//only insert here
                    Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to db");
                    MesTasks_WIP.Add(mesTask);
                    Log.Information("mesTask " + mesTask.TaskNoFromMes + " is initilized and upsert to WIP");
                    OnSingleMesTaskChange(mesTask);
                    await CancelWIPTask(originalTask.TaskNoFromMes);
                    return (true, "assign and cancle success");

                }
                else
                {
                    Log.Information("assign mesTask " + reviseTask.NewTaskMesNo + " fail");
                    return (false, "error occured when assigning to swarmcore (" + msg + ")");
                }
            }
            catch (Exception e)
            {
                Log.Information("assign mesTask " + reviseTask.NewTaskMesNo + " fail");
                return (false, "error occured when assigning to swarmcore");
            }
        }

        public async Task UpdateWIPMesTaskStatus()
        {
            await UpdateSwarmCoreTaskStatus();
            //update actual task status
            await Task.Run(async () =>
            {
                foreach (MesTaskDetail mesTask_WIP in MesTasks_WIP)
                //Parallel.ForEach(MesTasks_WIP, async mesTask_WIP =>
                {
                    //get new state from swarmcore
                    if (swarmCoreTaskStatus.Any(x => x.flowid == mesTask_WIP.TaskNoFromSwarmCore))
                    {
                        if (swarmCoreTaskStatus.Any(x => x.flowid == mesTask_WIP.TaskNoFromSwarmCore && x.state != mesTask_WIP.Status))
                        {
                            FlowTaskStatus mesTaskState = swarmCoreTaskStatus.FirstOrDefault(x => x.flowid == mesTask_WIP.TaskNoFromSwarmCore);
                            mesTask_WIP.Status = mesTaskState.state;
                            switch (mesTaskState.state)
                            {
                                case 0://queue
                                    break;
                                case 1://start processing
                                    string amr = mesTaskState.amrid;
                                    await SwarmCoreStartProcessing(mesTask_WIP, amr);
                                    Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " assign to SwarmCore by background service");
                                    break;
                                case 2://finished (seldom happend)
                                    await RemoveFromWIP(mesTask_WIP, "auto");
                                    Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " completed and removed from WIP by Swarmcore");
                                    break;
                                case 3://fail
                                case 4://pause
                                    await SwarmCoreUpdateStatusFailPauseWithTimeLog(mesTask_WIP, mesTaskState.state);
                                    break;
                                case 5://cancel (seldom happend)
                                    await RemoveFromWIP(mesTask_WIP, "auto");
                                    Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " is canceled removed from WIP by Swarmcore");
                                    break;
                                default:
                                    break;
                            }
                            OnSingleMesTaskChange(mesTask_WIP);
                        }
                    }
                    else
                    {
                        //actual task not exist in swarmcore -> remove
                        if (mesTask_WIP.Status == 1)
                        {
                            await RemoveFromWIP(mesTask_WIP, "auto");
                            OnSingleMesTaskChange(mesTask_WIP);
                            Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " remove from WIP by background service");
                        }
                    }

                }
                //);
                await Task.Delay(500);
            });
        }

        //remove from WIP and set finish to db
        public async Task RemoveFromWIP(MesTaskDetail mesTask_WIP, string reason)
        {
            if (mesTask_WIP.Status != 2)
            {
                mesTask_WIP.Status = 2;
            }
            if (mesTask_WIP.FinishOrTimeoutTime.Trim() == "not yet")
            {
                mesTask_WIP.FinishOrTimeoutTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            }
            if (mesTask_WIP.FinishReason == "unknown")
            {
                mesTask_WIP.FinishReason = reason;
            }
            MesTasks_WIP.Remove(mesTask_WIP);
            Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " is removed from WIP");
            await UpsertMesTaskForDB(mesTask_WIP);
            Log.Information("mesTask " + mesTask_WIP.TaskNoFromMes + " updates finished time to DB");
            //RefreshGroupFlag(mesTask_WIP, false);
        }

        public event Action<MesTaskDetail>? SingleMesTaskChangeAct;
        //only used to notify UI about actual task status auto changed and test task changed manually
        private void OnSingleMesTaskChange(MesTaskDetail mesTask) => SingleMesTaskChangeAct?.Invoke(mesTask);

        public List<ImesTask> GetQueue()
        {
            return queueForGroup;
        }
        public event Action<List<ImesTask>>? QueueTaskChangeAct;
        private void OnQueueTaskChange() => QueueTaskChangeAct?.Invoke(queueForGroup);
        //private bool CheckFromCanAssign(ImesTask task)
        //{
        //	if (groups.Exists(x => x.elementList.Contains(task.FromStation) && x.occupied))
        //	{
        //		return false;
        //	}
        //	return true;
        //}
        //private bool CheckToCanAssign(ImesTask task)
        //{
        //	if (groups.Exists(x => x.elementList.Contains(task.ToStation) && x.occupied))
        //	{
        //		return false;
        //	}
        //	return true;
        //}

        //private void RefreshGroupFlag(ImesTask task, bool flagStatus)
        //{
        //	foreach (GroupClass group in groups)
        //	{
        //		if (group.elementList.Contains(task.FromStation) || group.elementList.Contains(task.ToStation))
        //		{
        //			group.occupied = flagStatus;
        //		}
        //	}
        //	OnGroupChange();
        //}

        //public async Task CheckTaskInGRoupQueue()
        //{
        //	if (queueForGroup.Count > 0)
        //	{
        //		for (int i = 0; i < queueForGroup.Count; i++)
        //		{
        //			if (CheckFromCanAssign(queueForGroup[i]) && CheckToCanAssign(queueForGroup[i]))
        //			{
        //				await GetNewMESTask(queueForGroup[i]);
        //				queueForGroup.Remove(queueForGroup[i]);
        //			}
        //		}
        //		OnQueueTaskChange();
        //	}
        //}

        //private void RefreshGroupFlag(MesTaskDetail task, bool flagStatus)
        //{
        //	foreach (GroupClass group in groups)
        //	{
        //		if (group.elementList.Contains(task.FromStation) || group.elementList.Contains(task.ToStation))
        //		{
        //			group.occupied = flagStatus;
        //		}
        //	}
        //	OnGroupChange();
        //}

        //public event Action<List<GroupClass>>? GroupChangeAct;
        //private void OnGroupChange() => GroupChangeAct?.Invoke(groups);


        public void RefreshSchedulingTasks(bool bot, bool top, ushort sec)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AGVDBContext>();
                //neither bot nor top
                if (!bot && !top)
                {
                    scheduleTasks = context.ImesTasks.AsNoTracking<ImesTask>().Where(x => !x.TaskNoFromMes.Contains("BOT") && !x.TaskNoFromMes.Contains("TOP") && x.TaskType == 0 && x.DelaySecond >= sec).ToList();
                }
                else
                {
                    //bot and top
                    if (bot && top)
                    {
                        scheduleTasks = context.ImesTasks.AsNoTracking<ImesTask>().Where(x => (x.TaskNoFromMes.Contains("BOT") || x.TaskNoFromMes.Contains("TOP")) && x.TaskType == 0 && x.DelaySecond >= sec).ToList();
                    }
                    //only bot
                    else if (bot && !top)
                    {
                        scheduleTasks = context.ImesTasks.AsNoTracking<ImesTask>().Where(x => x.TaskNoFromMes.Contains("BOT") && x.TaskType == 0 && x.DelaySecond >= sec).ToList();
                    }
                    //only top
                    else if (!bot && top)
                    {
                        scheduleTasks = context.ImesTasks.AsNoTracking<ImesTask>().Where(x => x.TaskNoFromMes.Contains("TOP") && x.TaskType == 0 && x.DelaySecond >= sec).ToList();
                    }
                }
                OnScheduleTasksChange();
            }
        }
        public bool GetSchedulingStatus()
        {
            return scheduling;
        }
        public void PauseScheduling()
        {
            scheduling = false;
            schedulingPause = true;
            OnSchedulingChange();
        }
        public void ResumeScheduling()
        {
            schedulingPause = false;
            scheduling = true;
            OnSchedulingChange();
        }
        public void StopScheduling()
        {
            schedulingPause = false;

            scheduling = false;
            OnSchedulingChange();
            scheduleTasks = new();
            OnScheduleTasksChange();

        }
        public async Task<(bool, string)> SkipSchedulingSec(ushort sec)
        {
            bool res = false;
            string resStr = "nothing executed";
            await Task.Run(() =>
            {
                int currentMin = scheduleTasks.Min(x => x.DelaySecond);
                if (sec >= currentMin)
                {
                    res = false;
                    resStr = sec + " is larger than current  delay second";
                }
                else
                {
                    foreach (var task in scheduleTasks)
                    {
                        task.DelaySecond -= sec;
                    }
                    OnScheduleTasksChange();
                    res = true;
                    resStr = "skip " + sec + " success";
                }
            });
            return (res, resStr);
        }
        public List<ImesTask> GetSchedulingTasks()
        {
            return scheduleTasks;
        }
        public async Task StartScheduling()
        {
            scheduling = true;
            OnSchedulingChange();
            if (schedulingPause)
            {
                ResumeScheduling();
                return;
            }
            while (scheduleTasks.Count() > 0)
            {
                await Task.Delay(1000);
                if (scheduling)
                {
                    for (int i = 0; i < scheduleTasks.Count(); i++)
                    {
                        scheduleTasks[i].DelaySecond -= 1;
                        if (scheduleTasks[i].DelaySecond <= 0)
                        {
                            var res = await GetNewMESTask(scheduleTasks[i]);
                            if (res.Item1)
                            {
                                Log.Information(scheduleTasks[i].TaskNoFromMes + ":" + res.Item2);
                            }
                            else
                            {
                                Log.Warning(scheduleTasks[i].TaskNoFromMes + ":" + res.Item2);
                            }
                            scheduleTasks.Remove(scheduleTasks[i]);
                        }
                    }
                    OnScheduleTasksChange();
                }

            }
            scheduling = false;
            OnSchedulingChange();
        }
        public event Action<List<ImesTask>>? ScheduleTasksChangeAct;
        private void OnScheduleTasksChange() => ScheduleTasksChangeAct?.Invoke(scheduleTasks);
        public event Action<bool>? SchedulingChangeAct;
        private void OnSchedulingChange() => SchedulingChangeAct?.Invoke(scheduling);

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
