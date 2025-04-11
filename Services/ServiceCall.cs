using AsterNET.Manager;
using AsterNET.Manager.Action;
using AsterNET.Manager.Event;
using Microsoft.IdentityModel.Tokens;
using System.Linq.Expressions;

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
            public int Count { get; set; }              // Contatore delle occorrenze
            public TimeSpan TotalDuration { get; set; } // Durata totale (se ancora necessaria)
            public DateTime StartTime { get; set; }     // Timestamp di inizio chiamata

            // Nuovi campi aggiunti
            public string CallerNumber { get; set; }    // Numero del chiamante
            public string CalleeNumber { get; set; }    // Numero del chiamato (se disponibile)
            public string LinkedId { get; set; }        // ID collegato per correlazione eventi
            public string Channel { get; set; }         // Canale della chiamata
            public string CallType { get; set; }       // Tipo chiamata (Entrata/Uscita)

            // Costruttore per inizializzare i valori di default
            public CallInfo()
            {
                Count = 1;
                TotalDuration = TimeSpan.Zero;
                StartTime = DateTime.Now;
                CallerNumber = string.Empty;
                CalleeNumber = string.Empty;
                LinkedId = string.Empty;
                Channel = string.Empty;
                CallType = "Unknown";
            }
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

                _manager.DialBegin += OnDialBegin;

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
            _logger.LogInformation($"1. Nuovo canale attivo: {e.Channel} - id:{e.UniqueId}");

            // Recupera il linkedid dagli attributi se presente
            string linkedId = e.Attributes.ContainsKey("linkedid") ? e.Attributes["linkedid"] : null;
            string callKey = !string.IsNullOrEmpty(linkedId) ? linkedId : e.UniqueId;

            string _callerNumber = e.CallerIdNum;
            bool flag_interno = false;

            // Verifica se il canale è il destinatario della chiamata
            if (e.Channel.StartsWith("PJSIP/4") || e.Channel.StartsWith("SIP/4"))
            {
                _logger.LogInformation("2.1. Chiamata da interno.");

                if ((_callerNumber.Length == 3 || _callerNumber.Length == 4) && _callerNumber.StartsWith("4"))
                {
                    _logger.LogInformation($"2.1.1. Chiamata da interno: {_callerNumber}");
                    flag_interno = true;
                }

                // Controllo se il numero è lungo 11 cifre e inizia con 0, 1 o 2
                //if (!string.IsNullOrEmpty(_callerNumber) && (_callerNumber.Length == 11 || _callerNumber.Length == 12) &&
                //    (_callerNumber.StartsWith("0") || _callerNumber.StartsWith("1") || _callerNumber.StartsWith("2")))
                //{
                //    var _contatto = await _callStatisticsService.CercaContattoAsync(_callerNumber);
                //    if (_contatto != null)
                //    {
                //        _logger.LogInformation($"2.1.2.1. Numero modificato per la ricerca: {_callerNumber} -> {_callerNumber.Substring(1)}");
                //        _callerNumber = _callerNumber.Substring(1); // Prende solo gli ultimi 10 caratteri   
                //    }
                //}
            }

            if (_callerNumber == "1000" || e.Channel.StartsWith("PJSIP/1000") || e.Channel.StartsWith("SIP/1000"))
            {
                _logger.LogInformation($"3. Chiamata da interno: {_callerNumber}");
                return;
            }

            try {

                if (!string.IsNullOrEmpty(_callerNumber) && !processedUniqueIds.Contains(callKey))
                {
                    // 1. Verifica se la chiamata esiste già nel database
                    bool chiamataEsistente = await _callStatisticsService.CheckExistingCallAsync(callKey);

                    if (chiamataEsistente)
                    {
                        _logger.LogInformation($"3.5. Chiamata già presente nel database - Key: {callKey}");
                        processedUniqueIds.Add(callKey); // Marca come processata comunque
                        return;
                    }

                    processedUniqueIds.Add(callKey); // Segna la chiave come processata

                    if (!callData.ContainsKey(callKey))
                    {
                        callData[callKey] = new CallInfo
                        {
                            Count = 0,
                            TotalDuration = TimeSpan.Zero,
                            StartTime = DateTime.Now,
                            CallerNumber = _callerNumber,
                            LinkedId = linkedId
                        };

                        var _contatto = await _callStatisticsService.CercaContattoAsync(_callerNumber);
                        string ragioneSociale = _contatto?.RagioneSociale ?? "Non registrato";
                        string tipoChiamata = "";//_contatto?.Interno == 1 ? "Uscita" : "Entrata";

                        // Controllo che la chiamata è in entrata o in uscita
                        if (_callerNumber == "410" || _callerNumber == "411" || _callerNumber == "412" || _callerNumber == "413"
                            || _callerNumber == "414" || _callerNumber == "415" || _callerNumber == "416" || _callerNumber == "418"
                            || _callerNumber == "419" || _callerNumber == "420" || _callerNumber == "421" || _callerNumber == "422"
                            || _callerNumber == "423" || _callerNumber == "499")
                        {
                            tipoChiamata = "Uscita";
                        }
                        else
                        {
                            tipoChiamata = "Entrata";
                        }

                        //if (_contatto != null)
                        //{
                        //    tipoChiamata = _contatto.Interno == 1 ? "Uscita" : "Entrata";
                        //}

                        await _callStatisticsService.RegisterCall(
                            _callerNumber,
                            string.Empty, // CalleeNumber vuoto inizialmente
                            ragioneSociale,
                            string.Empty, // Ragione sociale chiamato vuota inizialmente
                            callData[callKey].StartTime,
                            tipoChiamata,
                            callKey,
                            ragioneSociale); // Passa callKey (linkedId o UniqueId)

                        _logger.LogInformation($"4.3. Chiamata iniziata: {_callerNumber}, Key: {callKey} (LinkedId: {linkedId ?? "null"}, UniqueId: {e.UniqueId})");
                    }
                    else
                    {
                        callData[callKey].Count++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.StackTrace);
            }

            // Resto del codice per la gestione del CallerID...
            string numeroChiamante = e.CallerIdNum;
            string channel = e.Channel;

            if (!string.IsNullOrEmpty(numeroChiamante) && numeroChiamante.Length == 11 &&
                (numeroChiamante.StartsWith("0") || numeroChiamante.StartsWith("1") || numeroChiamante.StartsWith("2")))
            {
                _logger.LogInformation($"5. Numero modificato per la ricerca: {numeroChiamante} -> {numeroChiamante.Substring(1)}");
                numeroChiamante = numeroChiamante.Substring(1);
            }

            var contatto = await _callStatisticsService.CercaContattoAsync(numeroChiamante);
            string callerIdPersonalizzato;

            if (!e.Attributes.TryGetValue("destchannel", out var destChannel) || string.IsNullOrEmpty(destChannel))
            {
                _logger.LogWarning($"6. Canale di destinazione {destChannel} non trovato negli attributi dell'evento. CallerID non modificato.");
            }

            _logger.LogInformation($"7. Chiamata in arrivo da: {e.CallerIdNum} - canale({e.Channel}) verso il canale: {channel}");

            if (contatto != null)
            {
                string ragioneSocialeClean = ReplaceAccentedChars(contatto.RagioneSociale);
                string cittaClean = ReplaceAccentedChars(contatto.Citta);

                if (flag_interno)
                {
                    _logger.LogInformation($"8.1. Trovato nel database: {ragioneSocialeClean}, {cittaClean}");
                    callerIdPersonalizzato = $"{ragioneSocialeClean} ({cittaClean})";
                }
                else
                {
                    if (string.IsNullOrEmpty(ragioneSocialeClean) || string.IsNullOrEmpty(cittaClean))
                    {
                        _logger.LogInformation($"8.2.1 Trovato nel database: {ragioneSocialeClean}, {cittaClean}");
                        callerIdPersonalizzato = $"{numeroChiamante}";
                    }
                    else
                    {
                        _logger.LogInformation($"8.2.2. Trovato nel database: {ragioneSocialeClean}, {cittaClean}");
                        callerIdPersonalizzato = $"{numeroChiamante} - {ragioneSocialeClean} ({cittaClean})";
                    }
                }
            }
            else
            {
                _logger.LogInformation("8.3. Numero non trovato nel database. Impostazione CallerID predefinito.");
                callerIdPersonalizzato = $"{numeroChiamante} - Non registrato";
            }

            var setCallerId = new SetVarAction
            {
                Channel = channel,
                Variable = "CALLERID(name)",
                Value = callerIdPersonalizzato
            };

            try
            {
                _manager.SendAction(setCallerId);
                _logger.LogInformation($"9. CallerID aggiornato: {callerIdPersonalizzato}");
            }
            catch (System.TimeoutException ex)
            {
                _logger.LogError($"10. Timeout durante l'aggiornamento del CallerID: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"11. Errore durante l'aggiornamento del CallerID: {ex.Message}");
            }
        }

        private async void OnDialBegin(object sender, DialBeginEvent e)
        {
            _logger.LogInformation($"D: Evento ricevuto: {e.GetType().Name}");

            try
            {
                // Recupera il linkedid (identificatore univoco della chiamata)
                string linkedId = e.Attributes.ContainsKey("linkedid") ? e.Attributes["linkedid"] : null;

                if (string.IsNullOrEmpty(linkedId))
                {
                    _logger.LogWarning("D: Nessun linkedid trovato nell'evento DialBegin");
                    return;
                }

                // Recupera il numero del chiamato dagli attributi
                string calledNumber = e.Attributes.ContainsKey("destcalleridnum") ? e.Attributes["destcalleridnum"] : null;

                if (string.IsNullOrEmpty(calledNumber))
                {
                    _logger.LogWarning("D: Numero chiamato non disponibile negli attributi");
                    return;
                }

                _logger.LogInformation($"D: Chiamata collegata - LinkedId: {linkedId}, Numero chiamato: {calledNumber}");

                // Recupera anche il nome del chiamato se disponibile
                string calledName = e.Attributes.ContainsKey("destcalleridname") ? e.Attributes["destcalleridname"] : string.Empty;

                // 1. Cerca nella callData in memoria
                if (callData.TryGetValue(linkedId, out var callInfo))
                {
                    callInfo.CalleeNumber = calledNumber;
                    _logger.LogInformation($"D: Aggiornata callData in memoria - LinkedId: {linkedId}");
                }

                // Controllo che il numero che viene chiamato non contenga gli 0,1,2 o altro inizialmente,
                // poichè uno di questi prefissi vengono generalemnte inseriti per chiamare all'eterno.
                if (!string.IsNullOrEmpty(calledNumber) && calledNumber.Length >= 11) {
                    if (calledNumber.StartsWith("0") || calledNumber.StartsWith("1") || calledNumber.StartsWith("2"))
                    {
                        var _contatto = await _callStatisticsService.CercaContattoAsync(calledNumber);
                        if (_contatto == null)
                        {
                            _logger.LogInformation($"D: Numero modificato per la ricerca: {calledNumber} -> {calledNumber.Substring(1)}");
                            calledNumber = calledNumber.Substring(1); // Prende solo gli ultimi 10 caratteri   
                        }
                    }
                }

                // 2. Aggiorna il record nel database
                bool success = await _callStatisticsService.UpdateCalledNumberAsync(
                    linkedId: linkedId,
                    calledNumber: calledNumber,
                    calledName: calledName);

                if (success)
                {
                    _logger.LogInformation($"D: Database aggiornato - LinkedId: {linkedId}, Numero chiamato: {calledNumber}");
                }
                else
                {
                    _logger.LogWarning($"D: Fallito aggiornamento database per LinkedId: {linkedId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"D: Errore durante DialBegin: {ex.Message}");
            }
        }

        private async void HandleHangupEvent(object sender, HangupEvent e)
        {
            _logger.LogInformation($"Catturato evento hangup: {e.Channel} - id:{e.UniqueId}");

            string hangupUniqueId = e.UniqueId;
            string linkedId = e.Attributes.ContainsKey("linkedid") ? e.Attributes["linkedid"] : null;
            DateTime endTime = DateTime.Now;

            if (string.IsNullOrEmpty(linkedId))
            {
                _logger.LogWarning("HangupEvent senza linkedId - impossibile aggiornare");
                return;
            }

            // 1. Prima prova ad aggiornare usando solo il linkedId
            bool success = await _callStatisticsService.UpdateCallEndTimeAsync(linkedId, endTime);

            if (!success)
            {
                // 2. Fallback: cerca nella callData in memoria
                if (callData.TryGetValue(linkedId, out var callInfo))
                {
                    _logger.LogInformation($"H: Fallback su callData - Key: {linkedId}");
                    success = await _callStatisticsService.UpdateCallEndTimeAsync(
                        linkedId: linkedId,
                        endTime: endTime,
                        startTime: callInfo.StartTime,
                        callerNumber: callInfo.CallerNumber);
                }
                else
                {
                    _logger.LogWarning($"H: Nessuna chiamata trovata in memoria per LinkedId: {linkedId}");
                }
            }

            // Rimuovi dalla callData indipendentemente dall'esito
            callData.Remove(linkedId);
        }

        private string ReplaceAccentedChars(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var accentedChars = new Dictionary<char, string>()
            {
                {'à', "a'"}, {'è', "e'"}, {'é', "e'"}, {'ì', "i'"}, {'ò', "o'"}, {'ù', "u'"},
                {'À', "A'"}, {'È', "E'"}, {'É', "E'"}, {'Ì', "I'"}, {'Ò', "O'"}, {'Ù', "U'"},
                {'á', "a'"}, {'ë', "e'"}, {'ï', "i'"}, {'ö', "o'"}, {'ü', "u'"},
                {'Á', "A'"}, {'Ë', "E'"}, {'Ï', "I'"}, {'Ö', "O'"}, {'Ü', "U'"}
            };

            var output = new System.Text.StringBuilder();
            foreach (char c in input)
            {
                if (accentedChars.TryGetValue(c, out string replacement))
                    output.Append(replacement);
                else
                    output.Append(c);
            }

            return output.ToString();
        }
    }
}
