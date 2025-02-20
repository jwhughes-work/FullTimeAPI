using FullTimeAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient<IFixturesService, FixturesService>();
builder.Services.AddHttpClient<IResultsService, ResultsService>();
builder.Services.AddHttpClient<ILeagueService, LeagueService>();
builder.Services.AddHttpClient<IClubService, ClubService>();
builder.Services.AddMemoryCache();
builder.Services.AddLogging();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
