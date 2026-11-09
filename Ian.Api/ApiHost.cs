using Ian.Infrastructure;
using Microsoft.AspNetCore.HttpLogging;

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
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "O";
        });

        var app = builder.Build();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpLogging();
        app.UseHttpsRedirection();

        app.MapGet("/app/v1/health", () => Results.Ok(new { status = "Healthy" }));

        return app;
    }
}
