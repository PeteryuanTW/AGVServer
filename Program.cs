using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using AGVServer.Service;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using NModbus;
using System.Net.Sockets;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddDevExpressBlazor(options => {
    options.BootstrapVersion = DevExpress.Blazor.BootstrapVersion.v5;
    options.SizeMode = DevExpress.Blazor.SizeMode.Large;
});



builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream" });
});



builder.WebHost.UseWebRoot("wwwroot");
builder.WebHost.UseStaticWebAssets();



//builder.Services.AddSignalR();
//builder.Services.AddSingleton<HubLog>();


var app = builder.Build();

app.UseResponseCompression();

//app.MapHub<SignalRHub>("/SignalrHub");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();


app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

#region ModbusTcp Slave thread
int port = 502;
IPAddress address = new IPAddress(new byte[] { 127, 0, 0, 1 });
// create and start the TCP slave
TcpListener slaveTcpListener = new TcpListener(address, port);
slaveTcpListener.Start();
IModbusFactory factory = new ModbusFactory();
IModbusSlaveNetwork network = factory.CreateSlaveNetwork(slaveTcpListener);
IModbusSlave slave1 = factory.CreateSlave(1);
network.AddSlave(slave1);
var bgThread = new Thread(() =>
{
    network.ListenAsync().GetAwaiter().GetResult();
});
bgThread.IsBackground = true;
bgThread.Start();
#endregion

app.Run();