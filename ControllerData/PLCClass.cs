using AGVServer.EFModels;
using DevExpress.ClipboardSource.SpreadsheetML;
using DevExpress.Pdf.Native.BouncyCastle.Utilities.Net;
using Microsoft.VisualBasic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AGVServer.Data
{
	public class PLCClass
	{
		public string ip { get; set; }
		public ushort port { get; set; }
		public string name { get; set; }
		public string type { get; set; }
		public string plcType { get; set; }
		public TcpClient tcpClient { get; set; }
		public ushort startIndex { get; set; }

		public List<PLCValueTable> valueTables { get; set; }
		public bool tcpConnect { get; set; }
		public bool tryingConnect { get; set; }
		public bool keepUpdate { get; set; }
		public PLCClass(Plcconfig plcconfig, List<MxmodbusIndex> typeIndexTable)
		{
			System.Net.IPAddress tmp_ip;
			if (System.Net.IPAddress.TryParse(plcconfig.Ip, out tmp_ip))
			{
				switch (tmp_ip.AddressFamily)
				{
					case AddressFamily.InterNetwork:
						ip = plcconfig.Ip;
						break;
					case AddressFamily.InterNetworkV6:
						break;
					default:
						break;
				}
			}
			port = (ushort)plcconfig.Port;
			this.name = plcconfig.Name;
			this.startIndex = (ushort)plcconfig.ModbusStartAddress;
			this.type = plcconfig.Type;
			this.plcType = plcconfig.Plctype;

			//init value mx modbus index table
			this.valueTables = new();
			if (typeIndexTable != null)
			{
				foreach (MxmodbusIndex type in typeIndexTable)
				{
					int tmpModbusIndex = this.startIndex + (ushort)type.Offset;
					if (!valueTables.Exists(x => x.modbusIndex == tmpModbusIndex))
					{
						valueTables.Add(new PLCValueTable
						{
							mxIndex = (ushort)type.MxIndex,
							modbusIndex = (ushort)(this.startIndex + (ushort)type.Offset),
							modbusValue = false,
							mxValue = false,
							mxSuccessWrite = false,
							mxSuccessRead = false,
						});
					}

				}
			}

			this.keepUpdate = plcconfig.Enabled;
			tryingConnect = false;
		}

		public PLCClass()
		{
			tcpConnect = false;
		}

		public void ResetValueTables()
		{
			foreach (PLCValueTable plcValueTable in valueTables)
			{
				plcValueTable.mxValue = false;
				plcValueTable.mxSuccessWrite = false;
				plcValueTable.mxSuccessRead = false;
			}
		}

		public async Task TryConnectTcp()
		{
			tryingConnect = true;
			try
			{
				tcpClient = new TcpClient();
				await tcpClient.ConnectAsync(ip, port);
				tcpConnect = true;
			}
			catch (Exception ex)
			{
				tcpConnect = false;
				Console.WriteLine(ex.ToString());
			}
			tryingConnect = false;

		}

		public Task TryDisconnect()
		{
			if (tcpConnect)
			{
				try
				{
					tcpClient.Close();
					tcpConnect = false;
					ResetValueTables();
				}
				catch (Exception e)
				{
				}
			}
			return Task.CompletedTask;
		}
		public async Task<(bool, bool)> ReadSingleM_MX(ushort index)//return (value, no error -> true)
		{
			byte[] header = { 0x50, 0x00, 0x00, 0xff, 0xff, 0x03, 0x00, 0x0c, 0x00, 0x00, 0x00, };
			byte[] cmd = { 0x01, 0x04, };
			byte[] subCmd = { 0x01, 0x00, };
			byte[] mxIndex = BitConverter.GetBytes(index).Concat(new byte[] { 0x00 }).ToArray();
			byte[] device = { 0x90, };
			byte[] points = { 0x02, 0x00 };
			byte[] strSend = header.Concat(cmd).Concat(subCmd).Concat(mxIndex).Concat(device).Concat(points).ToArray();

			try
			{
				NetworkStream nwStream = tcpClient.GetStream();
				while (!nwStream.CanWrite)
				{
					await Task.Delay(10);
				}
				nwStream.Write(strSend, 0, strSend.Length);
				byte[] res = new byte[12];//11 + 1
				while (!nwStream.CanRead)
				{
					await Task.Delay(10);
				}
				nwStream.ReadTimeout = 1000;
				nwStream.Read(res, 0, res.Length);

				Console.Write("Read at " + DateTime.Now.ToString("tt hh:mm:ss.fff")+":");
				foreach (var a in res)
				{
					Console.Write(a.ToString("x")+" ");
				}
				Console.WriteLine();
				if (res[9] == 0 && res[10] == 0)
				{
					string returnByteString = res[11].ToString("x2");
					if (returnByteString[0] == '0')
					{
						return (false, true);
					}
					else
					{
						return (true, true);
					}
				}
				else
				{
					return (false, false);
				}
			}
			catch (Exception ex)
			{
				return (false, false);
			}
		}


		public async Task<(bool, bool, bool)> ReadPairM_MX(ushort index)//return (value, next value, no errot -> true)
		{
			byte[] header = { 0x50, 0x00, 0x00, 0xff, 0xff, 0x03, 0x00, 0x0c, 0x00, 0x00, 0x00, };
			byte[] cmd = { 0x01, 0x04, };
			byte[] subCmd = { 0x01, 0x00, };
			byte[] mxIndex = BitConverter.GetBytes(index).Concat(new byte[] { 0x00 }).ToArray();
			byte[] device = { 0x90, };
			byte[] points = { 0x02, 0x00 };
			byte[] strSend = header.Concat(cmd).Concat(subCmd).Concat(mxIndex).Concat(device).Concat(points).ToArray();

			try
			{
				NetworkStream nwStream = tcpClient.GetStream();
				while (!nwStream.CanWrite)
				{
					await Task.Delay(10);
				}
				nwStream.Write(strSend, 0, strSend.Length);
				byte[] res = new byte[12];//11 + 1
				while (!nwStream.CanRead)
				{
					await Task.Delay(10);
				}
				nwStream.Read(res, 0, res.Length);
				if (res[9] == 0 && res[10] == 0)
				{
					string returnByteString = res[11].ToString("x2");
					switch ((returnByteString[0], returnByteString[1]))
					{
						case ('0', '0'):
							return (false, false, true);
						case ('0', '1'):
							return (false, true, true);
						case ('1', '0'):
							return (true, false, true);
						case ('1', '1'):
							return (true, true, true);
						default:
							return (false, false, false);
					}
				}
				else
				{
					return (false, false, false);
				}
			}
			catch (Exception ex)
			{
				return (false, false, false);
			}
		}
		public async Task<bool> WriteSingleM_MX(ushort index, bool val)//success write or not
		{
			(bool, bool, bool) pairCoil = await ReadPairM_MX((ushort)(index));
			if (!pairCoil.Item3)//read fail
			{
				return false;
			}
			else
			{
				if (pairCoil.Item1 == val)//no need to write
				{
					return true;
				}
				byte[] header = { 0x50, 0x00, 0x00, 0xff, 0xff, 0x03, 0x00, 0x0d, 0x00, 0x00, 0x00, };
				byte[] cmd = { 0x01, 0x14, };
				byte[] subCmd = { 0x01, 0x00, };
				byte[] mxIndex = BitConverter.GetBytes(index).Concat(new byte[] { 0x00 }).ToArray();
				byte[] device = { 0x90, };
				byte[] points = { 0x02, 0x00 };
				byte[] inputVal;
				if (pairCoil.Item2) //X1
				{
					if (val)
					{
						inputVal = new byte[] { 0x11, };
					}
					else
					{
						inputVal = new byte[] { 0x01, };
					}
				}
				else //X0
				{
					if (val)
					{
						inputVal = new byte[] { 0x10, };
					}
					else
					{
						inputVal = new byte[] { 0x00, };
					}
				}
				byte[] strSend = header.Concat(cmd).Concat(subCmd).Concat(mxIndex).Concat(device).Concat(points).Concat(inputVal).ToArray();

				try
				{
					NetworkStream nwStream = tcpClient.GetStream();
					nwStream.Write(strSend, 0, strSend.Length);
					byte[] res = new byte[11];//fixed 11
					nwStream.Read(res, 0, res.Length);
					Console.Write("Write at " + DateTime.Now.ToString("tt hh:mm:ss.fff")+" ");
					foreach (var a in res)
					{
						Console.Write(a);
					}
					if (res[9] == 0 && res[10] == 0)
					{
						return true;
					}
					else
					{
						return false;
					}
				}
				catch (Exception ex)
				{
					return false;
				}
			}
		}

		public void SelfCheck()
		{
			int failCount = valueTables.Count(x => x.mxSuccessRead);
			if (failCount == 0)
			{
				TryDisconnect();
			}
		}
	}
}
