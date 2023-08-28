using System.Threading;

namespace AGVServer.Service
{
	public class TokenUpdateService : BackgroundService
	{
		private readonly DataBufferService _dataBufferService;
		private readonly ConfigService _configService;
		private int refreshTokenHour;

		public TokenUpdateService(DataBufferService dataBufferService, ConfigService configService)
		{
			_dataBufferService = dataBufferService;
			_configService = configService;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			refreshTokenHour = _configService.GetTokenUpdateDay();
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
				
				await Task.Delay(refreshTokenHour * 60*60*1000);
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
