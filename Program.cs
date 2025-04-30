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
            // Vengono terminati eventuali processi duplicati prima di tutto
            TerminaProcessiDuplicati();

            if (args.Length > 0 && args[0].ToLower() == "setup")
            {
                Console.WriteLine("Modalità setup rilevata.");
                var app = new Application();
                app.Run(new MainWindow()); // Pannello WPF da ConfigTool
                return;
            }

            AvviaServer(args);
        }

        static void AvviaServer(string[] args)
        {
            try
            {
                var builder = WebApplication.CreateBuilder(args);

                

                // Logging
                builder.Logging.AddSimpleConsole(options =>
                {
                    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                    options.IncludeScopes = false;
                    options.SingleLine = true;
                    options.UseUtcTimestamp = false;
                });

                // Servizi
                builder.Services.AddSingleton<ServiceCall>();
                builder.Services.AddHostedService<AmiBackgroundService>();
                builder.Services.AddSingleton<DatabaseService>();

                builder.Services.AddControllers();
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();

                var app = builder.Build();

                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                }

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
