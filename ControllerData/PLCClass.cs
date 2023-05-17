using AGVServer.EFModels;
using DevExpress.ClipboardSource.SpreadsheetML;
using DevExpress.Pdf.Native.BouncyCastle.Utilities.Net;
using System.Net;
using System.Net.Sockets;

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
					case System.Net.Sockets.AddressFamily.InterNetwork:
						ip = plcconfig.Ip;
						break;
					case System.Net.Sockets.AddressFamily.InterNetworkV6:
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
							vlaue = false,
							alive = false,
						});
					}

				}
			}

			this.keepUpdate = plcconfig.Enabled;
			tryingConnect = false;
		}

		public void ResetValueTables()
		{
			foreach (PLCValueTable plcValueTable in valueTables)
			{
				plcValueTable.vlaue = false;
				plcValueTable.alive = false;
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
		public Task<(bool, bool)> ReadSingleM_MX(ushort index)//return (value, no error flag)
		{
			byte[] header = { 0x50, 0x00, 0x00, 0xff, 0xff, 0x03, 0x00, 0x0c, 0x00, 0x04, 0x00, };
			byte[] cmd = { 0x01, 0x04, };
			byte[] subCmd = { 0x01, 0x00, };
			byte[] mxIndex = BitConverter.GetBytes(index).Concat(new byte[]{0x00}).ToArray();
			byte[] device = { 0x90, };
			byte[] points = { 0x02, 0x00 };
			byte[] strSend = header.Concat(cmd).Concat(subCmd).Concat(mxIndex).Concat(device).Concat(points).ToArray();

			try
			{
				NetworkStream nwStream = tcpClient.GetStream();
				nwStream.Write(strSend, 0, strSend.Length);
				byte[] res = new byte[12];//11 + 1
				nwStream.Read(res, 0, res.Length);
				if (res[9] == 0 && res[10] == 0)
				{
					string returnByteString = res[11].ToString("x2");
					if (returnByteString[0] == '0')
					{
						return Task.FromResult((false, true));
					}
					else
					{
						return Task.FromResult((true, true));
					}
				}
				else
				{
					return Task.FromResult((false, false));
				}
			}
			catch (Exception ex)
			{
				return Task.FromResult((false, false));
			}
		}
		
	}
}
