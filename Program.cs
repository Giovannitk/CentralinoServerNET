using ServerCentralino.Services;

namespace ServerCentralino
    {
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                // Chiudi eventuali istanze esistenti di ServerCentralino
                System.Diagnostics.Process[] existingProcesses;
                do
                {
                    // Trova tutti i processi con lo stesso nome
                    existingProcesses = System.Diagnostics.Process.GetProcessesByName("ServerCentralino");

                    // Escludi il processo corrente dalla terminazione
                    var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                    existingProcesses = existingProcesses.Where(p => p.Id != currentProcess.Id).ToArray();

                    if (existingProcesses.Length > 0)
                    {
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
                        // Pausa per permettere la chiusura dei processi
                        System.Threading.Thread.Sleep(300);
                    }
                } while (existingProcesses.Length > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore durante la chiusura dei processi esistenti: {ex.Message}");
            }

            try
            {
                var builder = WebApplication.CreateBuilder(args);

                // Configurazione logging con timestamp
                builder.Logging.AddSimpleConsole(options =>
                {
                    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                    options.IncludeScopes = false;
                    options.SingleLine = true;
                    options.UseUtcTimestamp = false;
                });

                // Registrazione dei servizi
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
    }
}