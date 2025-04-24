using ServerCentralino.Services;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);

// Configurazione logging con timestamp
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
    options.IncludeScopes = false;
    options.SingleLine = true;
    options.UseUtcTimestamp = false;
});

const string appName = "ServerCentralino";
bool createdNew;
var mutex = new Mutex(true, appName, out createdNew);

if (!createdNew)
{
    Console.WriteLine($"{appName} è già in esecuzione. Uscita...");
    return; // Chiudi l'applicazione
}

try
{
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

    // Rilascia il mutex quando l'applicazione termina
    app.Lifetime.ApplicationStopped.Register(() => mutex.ReleaseMutex());

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Errore avvio applicazione: {ex.Message}");
    mutex.ReleaseMutex();
}
