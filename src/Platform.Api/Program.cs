using Platform.Api.Operations;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.TraceIdentifier = Guid.NewGuid().ToString("N");
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
    await next(context);
});

app.MapOperationalEndpoints();
app.Run();

public partial class Program;
