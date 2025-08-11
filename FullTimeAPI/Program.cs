using FullTimeAPI.Services;
using FullTimeAPI.Services.Interfaces;
using FullTimeAPI.Middleware;
using FullTimeAPI.Extensions;
using Microsoft.OpenApi.Models;
using AspNetCoreRateLimit;
using System.Reflection;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "FullTime API",
        Description = "FullTime API is an open source RESTful service that scrapes football data from FullTime, providing easy access to fixtures, results, league tables, and player stats.",
    });
});

// Rate limiting configuration
builder.Services.AddOptions();
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// Add resilient HTTP clients
builder.Services.AddResilientHttpClients();

// Register services with named HTTP client
builder.Services.AddScoped<IFixturesService, FixturesService>();
builder.Services.AddScoped<IResultsService, ResultsService>();
builder.Services.AddScoped<ILeagueService, LeagueService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IPlayerService, PlayerService>();

builder.Services.AddLogging();

var app = builder.Build();

// Global exception handler
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// Rate limiting
app.UseIpRateLimiting();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
