using ServerCentralino.Services;
using System.Diagnostics;
using System.Windows;
using ConfigTool;

namespace ServerCentralino
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            TerminaProcessiDuplicati();

            if (args.Length > 0 && args[0].ToLower() == "setup")
            {
                Console.WriteLine("Modalità setup rilevata.");
                var app = new Application();
                app.Run(new MainWindow());
                return;
            }

            AvviaServer(args);
        }

        static void AvviaServer(string[] args)
        {
            try
            {
                bool silentMode = args.Any(a => a.Equals("silent", StringComparison.OrdinalIgnoreCase));

                var builder = WebApplication.CreateBuilder(args);

                // Logging
                if (silentMode)
                {
                    builder.Logging.ClearProviders();
                }
                else
                {
                    builder.Logging.AddSimpleConsole(options =>
                    {
                        options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                        options.IncludeScopes = false;
                        options.SingleLine = true;
                        options.UseUtcTimestamp = false;
                    });
                }

                // Servizi
                builder.Services.AddSingleton<ServiceCall>();
                builder.Services.AddHostedService<AmiBackgroundService>();
                builder.Services.AddSingleton<DatabaseService>();

                // CORS per Blazor WebAssembly
                builder.Services.AddCors(options =>
                {
                    options.AddPolicy("AllowBrowserClients", policy =>
                    {
                        policy.WithOrigins(
                            "http://localhost:5006",         // Blazor dev
                            "http://10.36.150.250",          // Blazor in LAN
                            "http://10.36.150.250:5000"      // Se necessario specificare porta
                        )
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                    });
                });

                builder.Services.AddControllers();
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();

                var app = builder.Build();

                // Middleware
                app.UseSwagger();
                app.UseSwaggerUI();

                app.UseCors("AllowBrowserClients");

                app.UseHttpsRedirection();
                app.UseAuthorization();
                app.MapControllers();

                app.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore avvio applicazione: {ex.Message}");
            }
        }

        static void TerminaProcessiDuplicati()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var existingProcesses = Process.GetProcessesByName("ServerCentralino")
                    .Where(p => p.Id != currentProcess.Id)
                    .ToArray();

                foreach (var process in existingProcesses)
                {
                    try
                    {
                        process.Kill();
                        Console.WriteLine($"Terminata istanza esistente con PID: {process.Id}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Errore durante la terminazione del processo {process.Id}: {ex.Message}");
                    }
                }

                if (existingProcesses.Length > 0)
                    Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore durante la chiusura dei processi esistenti: {ex.Message}");
            }
        }
    }
}
