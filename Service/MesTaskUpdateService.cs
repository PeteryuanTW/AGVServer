namespace AGVServer.Service
{
	public class MesTaskUpdateService : BackgroundService
	{
		private readonly DataBufferService _dataBufferService;
		private readonly ConfigService _configService;

		private int updateSecond = 1;

		public MesTaskUpdateService(DataBufferService dataBufferService, ConfigService configService)
		{
			_dataBufferService = dataBufferService;
			_configService = configService;

			updateSecond = _configService.GetMesTaskUpdateSecond();
		}
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await _dataBufferService.UpdateWIPMesTaskStatus();
				}
				catch (Exception e)
				{

				}
				await Task.Delay(updateSecond * 1000);
			}
		}
	}
}
