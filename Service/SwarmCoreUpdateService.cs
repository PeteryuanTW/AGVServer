using DevExpress.Blazor.Popup.Internal;

namespace AGVServer.Service
{
	public class SwarmCoreUpdateService : BackgroundService
	{
		private readonly DataBufferService _dataBufferService;
		private readonly ConfigService _configService;
		private int refreshSec;
		public SwarmCoreUpdateService(DataBufferService dataBufferService, ConfigService configService)
		{
			_dataBufferService = dataBufferService;
			_configService = configService;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			refreshSec = _configService.GetAPIUpdateSec();
			while (!stoppingToken.IsCancellationRequested)
			{
				if (_dataBufferService.GetBearerToken() != "")
				{
					
					//update swarm core api here
					try
					{
						if (_dataBufferService.loadout == null || _dataBufferService.loadin == null)
						{
							await _dataBufferService.UpdateFlowPattern();
						}
						await _dataBufferService.UpdateAMRStatus();

						_dataBufferService.SetswarmCoreUpdateFlag(true);
					}
					catch (Exception e)
					{
						_dataBufferService.SetswarmCoreUpdateFlag(false);
						Console.WriteLine("update swarm core data fail");
					}
				}
				else
				{
					_dataBufferService.SetswarmCoreUpdateFlag(false);
				}
				await Task.Delay(refreshSec * 1000);
			}
		}
	}
}
