using System.Threading;

namespace AGVServer.Service
{
	public class TokenUpdateService : BackgroundService
	{
		private readonly DataBufferService _dataBufferService;
		private readonly ConfigService _configService;
		private int refreshTokenDay;

		public TokenUpdateService(DataBufferService dataBufferService, ConfigService configService)
		{
			_dataBufferService = dataBufferService;
			_configService = configService;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			refreshTokenDay = _configService.GetTokenUpdateDay();
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await _dataBufferService.UpdateToken();
					_configService.SetTokenValid(true);
				}
				catch(Exception e)
				{
					await StopAsync(stoppingToken);
					Console.WriteLine("update swarm core token fail");
				}
				
				await Task.Delay(refreshTokenDay*86400*1000);
			}
		}
		public override async Task StopAsync(CancellationToken cancellationToken)
		{
			_configService.SetTokenValid(false);
			_dataBufferService.SetBearerToken("");
			await base.StopAsync(cancellationToken);
			

		}
	}
}
