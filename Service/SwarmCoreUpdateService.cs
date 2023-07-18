using DevExpress.Blazor.Popup.Internal;

namespace AGVServer.Service
{
	public class SwarmCoreUpdateService : BackgroundService
	{
		private readonly DataBufferService _dataBufferService;
		private readonly ConfigService _configService;
		private int refreshSec = 3;
		public SwarmCoreUpdateService(DataBufferService dataBufferService, ConfigService configService)
		{
			_dataBufferService = dataBufferService;
			_configService = configService;
			refreshSec = _configService.GetAPIUpdateSec();
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				if (_dataBufferService.GetBearerToken() != "")
				{
					
					//update swarm core api here
					try
					{
						//await _dataBufferService.UpdateSwarmCoreTaskStatus();
						await _dataBufferService.UpdateAMRStatus();

						_dataBufferService.SetswarmCoreUpdateFlag(true);
					}
					catch (Exception e)
					{
						_dataBufferService.SetswarmCoreUpdateFlag(false);
						Console.WriteLine(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")+" update swarm core data fail ("+e.ToString()+")");
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
