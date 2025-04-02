using ServerCentralino.Services;

var builder = WebApplication.CreateBuilder(args);

// Configurazione logging con timestamp
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
    options.IncludeScopes = false;
    options.SingleLine = true;
    options.UseUtcTimestamp = false;
});

// Registrazione di AmiService e AmiBackgroundService
builder.Services.AddSingleton<ServiceCall>();
builder.Services.AddHostedService<AmiBackgroundService>();

// Registrazione di DatabaseService
builder.Services.AddSingleton<DatabaseService>();

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
