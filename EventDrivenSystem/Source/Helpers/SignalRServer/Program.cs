using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddLogging();

// Load configuration settings based on the environment
var configuration = builder.Configuration;
var allowedOriginsString  = configuration.GetValue<string>("Cors:AllowedOrigins");
// Print allowed origins to the console
// Print allowed origins to the console
var logger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger("AllowedOriginsLogger");
var environment = builder.Environment.EnvironmentName;
logger.LogInformation("Current Environment: {Environment}", environment);

string[] allowedOrigins = allowedOriginsString?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();


if (allowedOrigins.Any())
{
    logger.LogInformation("Allowed CORS Origins: {Origins}", string.Join(", ", allowedOrigins));
}
else
{
    logger.LogWarning("No CORS origins configured.");
}
// Add CORS services and configure them to allow specific origins
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins",
    corsBuilder =>
    {
        corsBuilder.WithOrigins(allowedOrigins)
                   .AllowAnyHeader()
                   .AllowAnyMethod()
                   .AllowCredentials();
    });
});

// Listen on all interfaces so the container's published port is reachable
builder.WebHost.UseUrls("http://*:5091");

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

// Enable CORS
app.UseCors("AllowSpecificOrigins");

app.MapControllers();
app.MapHub<AggregationHub>("/aggregationHub");
app.MapHub<CountryHub>("/countryHub");
app.MapHub<ExchangeHub>("/exchangeHub");
app.MapHub<DataFeedHub>("/datafeedHub");
app.MapHub<DataIngestionHub>("/dataIngestionHub");
app.MapHub<AlertHub>("/alertIngestionHub");
app.MapHub<IndicatorHub>("/indicatorHub");
app.MapHub<OrderHub>("/orderHub");
app.MapHub<PortfolioHub>("/portfolioHub");
app.MapHub<RiskHub>("/riskHub");
app.MapHub<StrategyHub>("/strategyHub");
app.MapHub<StrategyHub>("/pivotMarkingHub");

app.Run();
