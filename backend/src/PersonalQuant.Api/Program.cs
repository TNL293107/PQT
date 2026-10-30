using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersonalQuant.Api.Configuration;
using PersonalQuant.Api.Diagnostics;
using PersonalQuant.Api.Endpoints;
using PersonalQuant.Application;
using PersonalQuant.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---------------------------------------------------------
builder.Services
    .AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.SectionName));

// Validated at start-up: a key too short to be a key fails the deployment
// rather than guarding writes with something guessable (ADR-027).
builder.Services
    .AddOptions<OperatorKeyOptions>()
    .Bind(builder.Configuration.GetSection(OperatorKeyOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// --- Layers ----------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- Diagnostics -----------------------------------------------------------
builder.Services.AddHealthChecks().AddLivenessCheck();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// --- HTTP ------------------------------------------------------------------
builder.Services.AddOpenApi();

var corsOptions = builder.Configuration
    .GetSection(CorsOptions.SectionName)
    .Get<CorsOptions>() ?? new CorsOptions();

builder.Services.AddCors(options => options.AddPolicy(
    CorsOptions.PolicyName,
    policy => policy
        .WithOrigins(corsOptions.ParseAllowedOrigins())
        .WithHeaders("Content-Type", "Accept", "Authorization")
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

var app = builder.Build();

// --- Pipeline --------------------------------------------------------------
app.UseExceptionHandler();

// The OpenAPI document and its reference UI are development-only. There is no
// authentication yet, so the schema is not exposed outside development.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Personal Quant Terminal API"));
}

app.UseCors(CorsOptions.PolicyName);
app.UseOperatorKey();

app.MapDiagnosticsEndpoints();
app.MapInstrumentEndpoints();
app.MapMarketDataEndpoints();
app.MapCorporateActionEndpoints();
app.MapWatchlistEndpoints();
app.MapWorkspaceEndpoints();
app.MapUniverseEndpoints();
app.MapCoverageEndpoints();
app.MapFundamentalsEndpoints();
app.MapResearchEndpoints();

await app.RunAsync();

/// <summary>
/// Entry point marker.
/// </summary>
/// <remarks>
/// Declared explicitly so that <c>WebApplicationFactory&lt;Program&gt;</c> in
/// the integration tests can reference the host built by the statements above.
/// </remarks>
public partial class Program;
