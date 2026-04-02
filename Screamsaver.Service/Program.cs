using Microsoft.Extensions.Logging;
using Screamsaver.Core;
using Screamsaver.Core.Ipc;
using Screamsaver.Service;


var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = Screamsaver.Core.Constants.ServiceName;
});

builder.Services.AddSingleton<ISettingsRepository>(sp =>
    new SettingsRepository(
        new WindowsRegistryStore(Screamsaver.Core.Constants.RegistryKeyPath),
        sp.GetRequiredService<ILogger<SettingsRepository>>()));
builder.Services.AddSingleton<IPipeClient,         DefaultPipeClient>();
builder.Services.AddSingleton<IAudioMonitor,       AudioMonitor>();
builder.Services.AddSingleton<ITrayWatchdog,       TrayWatchdog>();
builder.Services.AddSingleton<IPipeServer,         PipeServer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
