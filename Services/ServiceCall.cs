using AsterNET.Manager;
using AsterNET.Manager.Action;
using AsterNET.Manager.Event;

namespace ServerCentralino.Services
{
    public class ServiceCall
    {
        private readonly ManagerConnection _manager;
        private readonly ILogger<ServiceCall> _logger;
        private readonly DatabaseService _callStatisticsService;

        private Dictionary<string, CallInfo> callData; // Memorizza le informazioni sulle chiamate
        private HashSet<string> processedUniqueIds; // Memorizza gli UniqueId già processati
        private readonly object _lock = new object(); // Oggetto per il locking

        private class CallInfo
        {
            public int Count { get; set; }
            public TimeSpan TotalDuration { get; set; }
            public DateTime StartTime { get; set; }
        }

        public ServiceCall(IConfiguration configuration, ILogger<ServiceCall> logger, DatabaseService callStatisticsService)
        {
            _logger = logger;
            _callStatisticsService = callStatisticsService;

            // Inizializza callData
            callData = new Dictionary<string, CallInfo>();
            processedUniqueIds = new HashSet<string>();

            string? amiHost = configuration["AmiSettings:Host"];
            string? amiUser = configuration["AmiSettings:Username"];
            string? amiPassword = configuration["AmiSettings:Password"];

            if (string.IsNullOrWhiteSpace(amiHost) || string.IsNullOrWhiteSpace(amiUser) || string.IsNullOrWhiteSpace(amiPassword))
            {
                throw new ArgumentException("Le credenziali AMI non sono configurate correttamente.");
            }

            if (!int.TryParse(configuration["AmiSettings:Port"], out int amiPort))
            {
                throw new ArgumentException("Il valore della porta AMI non è valido.");
            }

            _manager = new ManagerConnection(amiHost, amiPort, amiUser, amiPassword);
        }


        public void Start()
        {
            try
            {
                _manager.Login();
                _logger.LogInformation("Connesso ad Asterisk via AMI.");
                _manager.NewChannel += OnNewChannel;
                //_manager.DialBegin += HandleDialBeginEvent;
                _manager.Hangup += HandleHangupEvent; // Aggiungi questa linea

                _logger.LogInformation("AMI Service started and listening for events...");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore nella connessione ad AMI: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                if (_manager != null && _manager.IsConnected())
                {
                    _manager.Logoff();
                    _logger.LogInformation("AMI Service stopped.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante la disconnessione da AMI: {ex.Message}");
            }
        }


        private void OnAsteriskEvent(object sender, ManagerEvent e)
        {
            _logger.LogInformation($"Evento ricevuto: {e.GetType().Name}");
        }

        private void OnNewCallerId(object sender, NewCallerIdEvent e)
        {
            _logger.LogInformation($"Evento ricevuto: {e.GetType().Name}");
        }

        private async void OnNewChannel(object sender, NewChannelEvent e)
        {
            _logger.LogInformation($"Nuovo canale attivo: {e.Channel} - id:{e.UniqueId}");

            string uniqueId = e.UniqueId;
            string _callerNumber = e.CallerIdNum;

            // Verifica se il canale è il destinatario della chiamata
            if (e.Channel.StartsWith("PJSIP/4") || e.Channel.StartsWith("SIP/4"))
            {
                _logger.LogInformation("Chiamata da interno.");

                if ((_callerNumber.Length == 3 || _callerNumber.Length == 4) && _callerNumber.StartsWith("4"))
                {
                    _logger.LogInformation($"Chiamata da interno: {_callerNumber}");
                    return;
                }

                // Controllo se il numero è lungo 11 cifre e inizia con 0, 1 o 2
                if (!string.IsNullOrEmpty(_callerNumber) && (_callerNumber.Length == 11 || _callerNumber.Length == 12) &&
                    (_callerNumber.StartsWith("0") || _callerNumber.StartsWith("1") || _callerNumber.StartsWith("2")))
                {
                    var _contatto = await _callStatisticsService.CercaContattoAsync(_callerNumber);

                    if (_contatto != null)
                    {
                        _logger.LogInformation($"Numero modificato per la ricerca: {_callerNumber} -> {_callerNumber.Substring(1)}");
                        _callerNumber = _callerNumber.Substring(1); // Prende solo gli ultimi 10 caratteri   
                    }
                }
            }

            if (_callerNumber == "1000" || e.Channel.StartsWith("PJSIP/1000") || e.Channel.StartsWith("SIP/1000"))
            {
                _logger.LogInformation($"Chiamata da interno: {_callerNumber}");
                return;
            }

            if (!string.IsNullOrEmpty(_callerNumber) && !processedUniqueIds.Contains(uniqueId))
            {
                processedUniqueIds.Add(uniqueId); // Segna l'UniqueId come processato

                if (!callData.ContainsKey(uniqueId))
                {
                    callData[uniqueId] = new CallInfo { Count = 0, TotalDuration = TimeSpan.Zero };
                }
                callData[uniqueId].Count++;
                callData[uniqueId].StartTime = DateTime.Now;

                var _contatto = await _callStatisticsService.CercaContattoAsync(_callerNumber);
                string ragioneSociale = _contatto != null ? _contatto.RagioneSociale : "Non registrato";
                await _callStatisticsService.RegisterCall(_callerNumber, ragioneSociale, 0, callData[uniqueId].StartTime); // Registra la chiamata

                _logger.LogInformation($"Chiamata iniziata: {_callerNumber}, UniqueId: {uniqueId}");
            }

            string numeroChiamante = e.CallerIdNum; // Numero del chiamante
            string channel = e.Channel; // Canale della chiamata in arrivo

            // Controllo se il numero è lungo 11 cifre e inizia con 0, 1 o 2
            if (!string.IsNullOrEmpty(numeroChiamante) && numeroChiamante.Length == 11 &&
                (numeroChiamante.StartsWith("0") || numeroChiamante.StartsWith("1") || numeroChiamante.StartsWith("2")))
            {
                _logger.LogInformation($"Numero modificato per la ricerca: {numeroChiamante} -> {numeroChiamante.Substring(1)}");
                numeroChiamante = numeroChiamante.Substring(1); // Prende solo gli ultimi 10 caratteri
            }

            // Cerca il chiamante nel database
            var contatto = await _callStatisticsService.CercaContattoAsync(numeroChiamante);

            string callerIdPersonalizzato;

            if (!e.Attributes.TryGetValue("destchannel", out var destChannel) || string.IsNullOrEmpty(destChannel))
            {
                _logger.LogWarning($"Canale di destinazione {destChannel} non trovato negli attributi dell'evento. CallerID non modificato.");
            }

            _logger.LogInformation($"Chiamata in arrivo da: {e.CallerIdNum} - canale({e.Channel}) verso il canale: {channel}");

            if (contatto != null)
            {
                _logger.LogInformation($"Trovato nel database: {contatto.RagioneSociale}, {contatto.Citta}");
                callerIdPersonalizzato = $"{numeroChiamante} - {contatto.RagioneSociale} ({contatto.Citta})"; //({contatto.Citta})";
            }
            else
            {
                _logger.LogInformation("Numero non trovato nel database. Impostazione CallerID predefinito.");
                callerIdPersonalizzato = $"{numeroChiamante} - Non registrato";
            }

            // Modifica il CallerId sul canale di destinazione
            var setCallerId = new SetVarAction
            {
                Channel = channel, // Canale di destinazione (telefono di ufficio)
                Variable = "CALLERID(name)",
                Value = callerIdPersonalizzato
            };

            try
            {
                // Invia l'azione in modo asincrono
                _manager.SendAction(setCallerId);
                _logger.LogInformation($"CallerID aggiornato: {callerIdPersonalizzato}");
            }
            catch (System.TimeoutException ex)
            {
                _logger.LogError($"Timeout durante l'aggiornamento del CallerID: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'aggiornamento del CallerID: {ex.Message}");
            }
        }


        private async void HandleHangupEvent(object sender, HangupEvent e)
        {
            _logger.LogInformation($"Catturato evento hangup: {e.Channel} - id:{e.UniqueId}");

            string uniqueId = e.UniqueId;
            string callerNumber = e.CallerIdNum;

            if (!string.IsNullOrEmpty(callerNumber) && callData.ContainsKey(uniqueId))
            {
                DateTime endTime = DateTime.Now;
                TimeSpan duration = endTime - callData[uniqueId].StartTime;
                callData[uniqueId].TotalDuration += duration;

                _logger.LogInformation($"Chiamata terminata: {callerNumber}, UniqueId: {uniqueId}, Durata: {duration.TotalSeconds} secondi");

                // Aggiorna la durata della chiamata nel registro
                await _callStatisticsService.UpdateCallDuration(callerNumber, duration.TotalSeconds, endTime);

                // Rimuovi la voce da callData dopo averla elaborata
                callData.Remove(uniqueId);
            }
        }
    }
}
