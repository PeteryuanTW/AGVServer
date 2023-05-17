using AGVServer.EFModels;

namespace AGVServer.Service
{
	public class PLCUpdateService : BackgroundService
	{
		private readonly DataBufferService _dataBufferService;
		private readonly ConfigService _configService;


		public PLCUpdateService(DataBufferService dataBufferService, ConfigService configService)
		{
			_dataBufferService = dataBufferService;
			_configService = configService;
		}
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			await _dataBufferService.InitPLCClass();
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await _dataBufferService.UpdatePLCStatus();
				}
				catch (Exception e)
				{
					Console.WriteLine(e);
				}
				int tmp = 5;
				await Task.Delay(1 * 1000);
			}
		}
	}
}
