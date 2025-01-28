using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// If you want to use the Aspire Service Defaults, uncomment the following lines
// builder.ConfigureOpenTelemetry();
//
// builder.AddDefaultHealthChecks();
//
// builder.Services.AddServiceDiscovery();
//
// builder.Services.ConfigureHttpClientDefaults(http =>
// {
//     // Turn on resilience by default
//     http.AddStandardResilienceHandler();
//
//     // Turn on service discovery by default
//     http.AddServiceDiscovery();
// });

// Metrics
var observeMeMeter = new Meter("Observe.Me", "1.0.0");
var countWeatherForeCasts =
    observeMeMeter.CreateCounter<int>("forecasts.count", description: "Counts the number of weather forecasts sent");

// Activities (the rest of the OTel world calls these Spans and are meant for tracing purposes
var observeMeActivitySource = new ActivitySource("Observe.Me");

// Setup logging to be exported via OpenTelemetry
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

var otel = builder.Services.AddOpenTelemetry();

// Add Metrics for ASP.NET Core and our custom metrics and export via OTLP
otel.WithMetrics(metrics =>
{
    // Metrics provider from OpenTelemetry
    metrics.AddAspNetCoreInstrumentation();
    metrics.AddMeter(observeMeMeter.Name);
    metrics.AddMeter("Microsoft.AspNetCore.Hosting");
    metrics.AddMeter("Microsoft.AspNetCore.Server.Kestrel");
});

// Add Tracing for ASP.NET Core and our custom ActivitySource and export via OTLP
otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddHttpClientInstrumentation();
    tracing.AddSource(observeMeActivitySource.Name);
});

// Export OpenTelemetry data via OTLP, using env vars for the configuration
// docker run --rm -it -p 18888:18888 -p 4317:18889 --name aspire-dashboard mcr.microsoft.com/dotnet/aspire-dashboard:latest
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (otlpEndpoint != null)
{
    otel.UseOtlpExporter();
}

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", SendWeatherForecast);

app.Run();
return;

Task<WeatherForecast[]> SendWeatherForecast()
{
    // Create a new Span scoped to the method
    using var activity = observeMeActivitySource.StartActivity("WeatherForecastActivity");
    
    // Log a message
    logger.LogInformation("Sending forecasts");
    
    var forecast = Enumerable.Range(1, 5).Select(index =>
            new WeatherForecast
            (
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                summaries[Random.Shared.Next(summaries.Length)]
            ))
        .ToArray();
    
    countWeatherForeCasts.Add(forecast.Length);
    
    // Add a tag to the Activity/Span
    activity?.SetTag("forecast", "Proudly provided by Observe.Me");
    
    return Task.FromResult(forecast);
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}