using Microsoft.Extensions.Hosting;
using NetCord.Gateway;
using NetCord.Hosting;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;

var appBuilder = Host.CreateApplicationBuilder(args);

string discordToken = appBuilder.Configuration["Discord:Token"]
    ?? throw new InvalidOperationException("Discord bot token is missing");

appBuilder.Services
    .AddDiscordGateway()
    .AddApplicationCommands();

var host = appBuilder.Build();

host.AddSlashCommand("hello", "Says hello!", () => "Hello, I'm John VGDC!");

host.AddModules(typeof(Program).Assembly);

await host.RunAsync();

