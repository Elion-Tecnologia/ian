using Ian.Infrastructure;
using Microsoft.AspNetCore.HttpLogging;
using Ian.Api.Endpoints;
using Ian.Infrastructure.Persistence;

namespace Ian.Api;

public static class ApiHost
{
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        builder.Services.AddOpenApi();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddHttpLogging(options =>
        {
            options.LoggingFields = HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
            options.CombineLogs = true;
        });

        builder.Logging.ClearProviders();
        builder.Logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId);

        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
                options.IncludeScopes = true;
            });
        }
        else
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "O";
            });
        }

        var app = builder.Build();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpLogging();
        app.UseHttpsRedirection();

        app.MapGet("/app/v1/health", () => Results.Ok(new { status = "Healthy" }));
        app.MapGet("/app/v1/ready", async (PostgresSchemaReadiness readiness, CancellationToken ct) =>
        {
            try
            {
                return await readiness.IsReadyAsync(ct)
                    ? Results.Ok(new { status = "Ready" })
                    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });
        app.MapTelegramWebhook();

        return app;
    }
}
