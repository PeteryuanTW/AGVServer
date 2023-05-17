using AGVServer.Data;
using AGVServer.EFModels;
using System.Reflection.Metadata.Ecma335;

namespace AGVServer.Service
{
	public class ConfigService
	{
		private readonly AGVDBContext _DBcontext;
		private readonly IServiceScopeFactory scopeFactory;

		public ConfigService(IServiceScopeFactory scopeFactory)
		{
			this._DBcontext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<AGVDBContext>();
			InitialVar();
		}

		private IEnumerable<Configuration> configurations;
		private string baseURL;
		private string port;
		private int APIUpdateSec;
		private int tokenUpdateDay;
		private bool APITokenValid = false;
		private DateTime tokenUpdateTime = DateTime.Now;
		private IEnumerable<Plcconfig> plcconfigs;

		public void InitialVar()
		{
			configurations = _DBcontext.Configurations.ToList();
            baseURL = configurations.First(x => x.ConfigName == "ip").ConfigValue;
			port = configurations.First(x => x.ConfigName == "port").ConfigValue;
			APIUpdateSec = Convert.ToInt32(configurations.First(x => x.ConfigName == "APIUpdateSecond").ConfigValue);
			tokenUpdateDay = Convert.ToInt32(configurations.First(x => x.ConfigName == "tokenUpdateDay").ConfigValue);

			plcconfigs = _DBcontext.Plcconfigs.ToList();
		}

		public string GetURLAndPort()
		{
			return "http://" + baseURL + ":" + port;
		}
		public int GetAPIUpdateSec()
		{
			return APIUpdateSec;
		}
		public int GetTokenUpdateDay()
		{
			return tokenUpdateDay;
		}

		public bool GetTokenValid()
		{
			return APITokenValid;
		}
		public void SetTokenValid(bool flag)
		{
			APITokenValid = flag;
			tokenUpdateTime = DateTime.Now;
			OnSwarmCoreTokenChange();
		}
		public DateTime GetTokenUpdateTime()
		{
			return tokenUpdateTime;
		}
		public event Action<bool, DateTime>? SwarmCoreTokenChangeAct;
		private void OnSwarmCoreTokenChange() => SwarmCoreTokenChangeAct?.Invoke(APITokenValid, tokenUpdateTime);


		public IEnumerable<Configuration> GetConfigs()
		{
			return configurations;
		}
		public void UpdateConfigs(IEnumerable<Configuration> newConfigs)
		{
			foreach (Configuration newConfig in newConfigs)
			{
				Configuration target = _DBcontext.Configurations.Find(newConfig.ConfigName);
				if (target != null && target.ConfigValue != newConfig.ConfigValue)
				{
					_DBcontext.Entry(target).CurrentValues.SetValues(newConfig.ConfigValue);
				}
			}
			_DBcontext.SaveChanges();

		}

		public IEnumerable<Plcconfig> GetPlcConfigs()
		{
			return plcconfigs;
		}
    }
}
