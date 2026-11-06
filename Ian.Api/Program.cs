var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/app/v1/health", () =>
{
    Console.WriteLine("Health checado. Endpoint acionado.");
    return Results.Ok(new { status = "Healthy" });
});

app.Run();