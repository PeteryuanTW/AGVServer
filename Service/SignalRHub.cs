using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http.Features;
using System.Net;
using AGVServer.Data;
using System.Reflection.Metadata.Ecma335;

namespace AGVServer.Service
{
	public class HubLog
	{
		private List<SignalRClient> ConnectedClients = new();
		public List<SignalRClient> GetConnectedClients()
			=> ConnectedClients;
		public void ClientChange(SignalRClient tarGetClient)
		{
			switch (tarGetClient.act)
			{
				case SignalRAction.Connect:
					ConnectedClients.Add(tarGetClient);
					break;
				case SignalRAction.Disconnect:
					ConnectedClients.Remove(tarGetClient);
					break;
				case SignalRAction.Send:
				case SignalRAction.Receive:
					foreach (SignalRClient signalRClient in ConnectedClients)
					{
						if (signalRClient.id == tarGetClient.id)
						{
							signalRClient.act = tarGetClient.act;
							signalRClient.time = DateTime.Now;
						}
					}
					break;
				default:
					break;
			}
			ClientCahngeAct?.Invoke(tarGetClient);
		}
		public Action<SignalRClient>? ClientCahngeAct;
	}
	public class SignalRHub : Hub
	{
		HubLog hubLog;
		public SignalRHub(HubLog hubLog)
		{
			this.hubLog = hubLog;
			//Console.WriteLine(hubLog.GetHashCode());
		}
		public override async Task OnConnectedAsync()
		{
			IPAddress connectIP = Context.Features.Get<IHttpConnectionFeature>().RemoteIpAddress;
			String id = Context.ConnectionId;
			hubLog.ClientChange(new SignalRClient(connectIP, id, SignalRAction.Connect));
			//Console.WriteLine(id + " connected at " + connectIP);
			await base.OnConnectedAsync();
		}
		public override Task OnDisconnectedAsync(Exception? exception)
		{
			IPAddress connectIP = Context.Features.Get<IHttpConnectionFeature>().RemoteIpAddress;
			String id = Context.ConnectionId;
			hubLog.ClientChange(new SignalRClient(connectIP, id, SignalRAction.Disconnect));
			//Console.WriteLine(id + " disconnected at " + connectIP);
			return base.OnDisconnectedAsync(exception);
		}

		public void SendMsg(string msg)
		{
			IPAddress connectIP = Context.Features.Get<IHttpConnectionFeature>().RemoteIpAddress;
			String id = Context.ConnectionId;
			hubLog.ClientChange(new SignalRClient(connectIP, id, SignalRAction.Send));
			Clients.All.SendAsync("ResponseFromServer", msg+"-response" +DateTime.Now.ToString());
		}


	}
}
