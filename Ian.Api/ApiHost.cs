using Ian.Infrastructure;
using Microsoft.AspNetCore.HttpLogging;
using Ian.Api.Endpoints;

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
        app.MapTelegramWebhook();

        return app;
    }
}
