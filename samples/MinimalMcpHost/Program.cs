using TxTextControl.McpServer;

if (await TextControlMcpWorker.RunIfRequestedAsync(args) is int workerExitCode)
{
    Environment.ExitCode = workerExitCode;
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTextControlMcpServer(builder.Configuration);
var app = builder.Build();
// Local development only. Add host authentication and session ownership checks before public deployment.
app.MapTextControlMcp();
app.Run();
