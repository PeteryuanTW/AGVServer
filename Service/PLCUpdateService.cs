using AGVServer.EFModels;
using NModbus;
using System.Net.Sockets;

namespace AGVServer.Service
{
	public class PLCUpdateService : BackgroundService
	{
		private readonly DataBufferService _dataBufferService;
		private readonly ConfigService _configService;

		private readonly TcpClient tcpClient;
		private readonly ModbusFactory factory;
		private IModbusMaster master;

		private int plcUpdateTick;


		public PLCUpdateService(DataBufferService dataBufferService, ConfigService configService)
		{
			_dataBufferService = dataBufferService;
			_configService = configService;
			
			tcpClient = new TcpClient("10.10.3.188", 502);
			factory = new ModbusFactory();
		}
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			plcUpdateTick = _configService.GetPLCUpdateSecond();
			await _dataBufferService.InitPLCClass();
			master = factory.CreateMaster(tcpClient);
			while (!stoppingToken.IsCancellationRequested)
			{
				if (_dataBufferService.GetPLCUpdateFlag())
				{
					try
					{
						if (_dataBufferService.GetPLCClasses().Count()>0)
						{
							await _dataBufferService.UpdatePLCStatus(master);
						}
					}
					catch (Exception e)
					{
						Console.WriteLine(e);
					}
					await Task.Delay(plcUpdateTick * 1000);
				}
			}
			tcpClient.Close();
			_dataBufferService.ResetPLCClass();

        }
	}
}
