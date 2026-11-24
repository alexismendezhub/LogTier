using Serilog;
using LogTier.Serilog;

var builder = WebApplication.CreateBuilder(args);

var logTier = new LogTierOptions
{
    ApplicationName = "LogTier.Serilog.Sample",
    ApplicationVersion = "1.0.0",
    EnvironmentName = builder.Environment.EnvironmentName,
    // Prefer a stable CMDB/asset identifier in production.
    DeviceIdentifier = Environment.GetEnvironmentVariable("LOGTIER_DEVICE_ID")
};

builder.Host.UseLogTierSerilog(
    logTier,
    (_, logger) => logger
        .MinimumLevel.Information()
        .WriteTo.LogTierConsole(logTier)
        .WriteTo.LogTierFile(
            logTier,
            path: "logs/logtier-.jsonl",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30));

var app = builder.Build();

// In production, call UseForwardedHeaders() before this middleware when a trusted reverse proxy/load balancer is used.
app.UseLogTierRequestLogging(logTier);

Log.Logger.LogTierApplicationStarted();

app.MapGet("/", () => Results.Ok(new { service = "LogTier.Serilog.Sample", status = "ok" }));

app.MapPost("/auth-demo", (AuthDemoRequest request) =>
{
    var succeeded = string.Equals(request.UserName, "demo", StringComparison.OrdinalIgnoreCase);
    Log.Logger.LogTierAuthenticationAttempt(
        succeeded,
        userId: request.UserName,
        userName: request.UserName);

    return succeeded ? Results.Ok() : Results.Unauthorized();
});

app.MapPost("/transaction-demo/{id}", (string id) =>
{
    Log.Logger.LogTierTransaction(
        transactionType: "DemoTransaction",
        transactionId: id,
        statusCode: "Success");

    return Results.Accepted();
});

try
{
    await app.RunAsync();
}
finally
{
    Log.Logger.LogTierApplicationStopped();
    Log.CloseAndFlush();
}

internal sealed record AuthDemoRequest(string UserName);
