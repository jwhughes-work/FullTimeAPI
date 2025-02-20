using FullTimeAPI.Services;
using FullTimeAPI.Services.Interfaces;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

builder.Services.AddHttpClient<IFixturesService, FixturesService>();
builder.Services.AddHttpClient<IResultsService, ResultsService>();
builder.Services.AddHttpClient<ILeagueService, LeagueService>();
builder.Services.AddHttpClient<ISearchService, SearchService>();
builder.Services.AddMemoryCache();
builder.Services.AddLogging();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
