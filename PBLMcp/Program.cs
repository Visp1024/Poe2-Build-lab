using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PBLMcp;

var builder = Host.CreateApplicationBuilder(args);

// MCP stdio transport uses stdout for JSON-RPC — suppress all console logging
builder.Logging.SetMinimumLevel(LogLevel.None);

builder.Services.AddSingleton<PBLState>();
builder.Services.AddSingleton<AppDriver>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(PBLTools).Assembly);

await builder.Build().RunAsync();
