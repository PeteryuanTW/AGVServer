using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using AGVServer.Service;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using NModbus;

using System.Net.Sockets;
using System.Net;
using Microsoft.OpenApi.Models;
using AGVServer.EFModels;
//using DevExpress.XtraPrinting.Shape.Native;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddHttpClient();
builder.Services.AddDevExpressBlazor(options => {
    options.BootstrapVersion = DevExpress.Blazor.BootstrapVersion.v5;
    options.SizeMode = DevExpress.Blazor.SizeMode.Large;
});



builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream" });
});

#region api & swagger
builder.Services.AddControllers();
builder.Services.AddControllersWithViews();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v0", new OpenApiInfo { Title = "TM", Version = "v0" });
});

#endregion


builder.WebHost.UseWebRoot("wwwroot");
builder.WebHost.UseStaticWebAssets();

builder.Services.AddDbContextFactory<AGVDBContext>();

//builder.Services.AddSignalR();
//builder.Services.AddSingleton<HubLog>();
#region Service
builder.Services.AddSingleton<DataBufferService>();
builder.Services.AddSingleton<ConfigService>();

builder.Services.AddSingleton<SwarmCoreUpdateService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SwarmCoreUpdateService>());


builder.Services.AddSingleton<TokenUpdateService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TokenUpdateService>());

builder.Services.AddSingleton<PLCUpdateService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PLCUpdateService>());
#endregion



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
//app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();
app.MapControllers();   

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

#region ModbusTcp Slave thread
int port = 502;
IPAddress address = new IPAddress(new byte[] { 10, 10, 3, 188 });
// create and start the TCP slave
TcpListener slaveTcpListener = new TcpListener(address, port);
slaveTcpListener.Start();
IModbusFactory factory = new ModbusFactory();
IModbusSlaveNetwork network = factory.CreateSlaveNetwork(slaveTcpListener);
IModbusSlave slave = factory.CreateSlave(1);

network.AddSlave(slave);
var bgThread = new Thread(() =>
{
    network.ListenAsync().GetAwaiter().GetResult();
});
bgThread.IsBackground = true;
bgThread.Start();
#endregion

#region swagger

app.UseSwagger();
app.UseSwaggerUI(c =>
{
	c.SwaggerEndpoint("/swagger/v0/swagger.json", "v0");
});

#endregion

app.Run();