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


		public PLCUpdateService(DataBufferService dataBufferService, ConfigService configService)
		{
			_dataBufferService = dataBufferService;
			_configService = configService;
			tcpClient = new TcpClient("192.168.132.200", 502);
			factory = new ModbusFactory();
		}
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			await _dataBufferService.InitPLCClass();
			master = factory.CreateMaster(tcpClient);
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await _dataBufferService.UpdatePLCStatus(master);
				}
				catch (Exception e)
				{
					Console.WriteLine(e);
				}
				int tmp = 5;
				await Task.Delay(1 * 1000);
			}
			tcpClient.Close();
		}
	}
}
