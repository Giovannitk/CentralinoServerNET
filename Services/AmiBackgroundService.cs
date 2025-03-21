using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ServerCentralino.Services
{
    public class AmiBackgroundService : BackgroundService
    {
        private readonly ServiceCall _amiService;
        private readonly ILogger<AmiBackgroundService> _logger;

        public AmiBackgroundService(ServiceCall amiService, ILogger<AmiBackgroundService> logger)
        {
            _amiService = amiService;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AmiBackgroundService avviato.");
            _amiService.Start();
            return Task.CompletedTask; // Il servizio rimane in ascolto degli eventi
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AmiBackgroundService in arresto...");
            _amiService.Stop();
            return base.StopAsync(cancellationToken);
        }
    }
}

