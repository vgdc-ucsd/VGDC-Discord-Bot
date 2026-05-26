using JohnVGDCBot;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord.Gateway;
using NetCord.Hosting;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using Quartz;

var appBuilder = Host.CreateApplicationBuilder(args);

var dbPath = Path.Combine(AppContext.BaseDirectory, "bot.db");

appBuilder.Services.AddDbContext<BotDbContext>(o =>
{
    o.UseSqlite($"Data Source={dbPath}");
});
appBuilder.Services
    .AddDiscordGateway()
    .AddApplicationCommands();
appBuilder.Services.AddQuartz(q =>
{
    q.UseInMemoryStore();
});
appBuilder.Services.AddQuartzHostedService(q =>
{
    q.WaitForJobsToComplete = true;
});

var host = appBuilder.Build();

host.AddSlashCommand("hello", "Says hello!", () => "Hello, I'm John VGDC!");
host.AddModules(typeof(Program).Assembly);

await host.RunAsync();

