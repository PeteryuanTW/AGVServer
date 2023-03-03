using System.Net;

namespace AGVServer.Data
{
	public enum SignalRAction
	{
		Connect,
		Send,
		Receive,
		Disconnect,
	}
	public class SignalRClient
	{
		public IPAddress ip { get; set; }
		public string id { get; set; }
		public SignalRAction act { get; set; }
		public DateTime time { get; set; }
		public SignalRClient(IPAddress ip, string id)
		{
			this.ip = ip;
			this.id = id;
			act = SignalRAction.Connect;
			time = DateTime.Now;
		}
		public SignalRClient(IPAddress ip, string id, SignalRAction act)
		{
			this.ip = ip;
			this.id = id;
			this.act = act;
			time = DateTime.Now;
		}
	}
}
