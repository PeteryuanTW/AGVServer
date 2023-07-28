namespace AGVServer.Service
{
	public class QueueUpdateService : BackgroundService
	{
		private readonly DataBufferService _dataBufferService;

		public QueueUpdateService(DataBufferService dataBufferService)
		{
			_dataBufferService = dataBufferService;
		}
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					//await _dataBufferService.CheckTaskInGRoupQueue();
				}
				catch (Exception e)
				{

				}
				await Task.Delay(1000);
			}
		}
	}
}
