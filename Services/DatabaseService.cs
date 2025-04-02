using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ServerCentralino.Services
{
    public class DatabaseService
    {
        private readonly string? _connectionString;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        public async Task<Contatto?> CercaContattoAsync(string numeroTelefono)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT RagioneSociale, CittaProvenienza
                FROM Rubrica
                WHERE NumeroContatto = @numero";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@numero", numeroTelefono);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return new Contatto
                                {
                                    RagioneSociale = reader["RagioneSociale"].ToString(),
                                    Citta = reader["CittaProvenienza"].ToString()
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"2.1.3. 4.1. 5.1. Errore di connessione al database: {ex.Message}");
            }

            return null;
        }

        public async Task RegisterCall(string numeroChiamante, string numeroChiamato, string ragioneSocialeChiamante, string ragioneSocialeChiamato, DateTime starttime, string tipoChiamata, string uniqueId, string locazione)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Trova o inserisce il numero del chiamante nella Rubrica
                            string idChiamante = await TrovaOInserisciNumeroAsync(connection, transaction, numeroChiamante);

                            // Trova o inserisce il numero del chiamato (puoi usare un valore predefinito o un altro numero)
                            string idChiamato = "0000000000"; //await TrovaOInserisciNumeroAsync(connection, transaction, numeroChiamato); // Usa un valore predefinito o personalizzato

                            // Data di arrivo della chiamata
                            DateTime dataArrivo = starttime;

                            // Inserisce la chiamata nella tabella Chiamate
                            string queryInserisciChiamata = @"
                        INSERT INTO Chiamate (NumeroChiamante, NumeroChiamato, TipoChiamata, DataArrivoChiamata, DataFineChiamata, RagioneSocialeChiamante, RagioneSocialeChiamato, UniqueId, Locazione)
                        VALUES (@chiamante, @chiamato, @tipo, @arrivo, @fine, @rsChiamante, @rsChiamato, @uniqueid, @locazione)";

                            using (var command = new SqlCommand(queryInserisciChiamata, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@chiamante", idChiamante);
                                command.Parameters.AddWithValue("@chiamato", idChiamato);
                                command.Parameters.AddWithValue("@tipo", tipoChiamata); // Tipo di chiamata (puoi personalizzarlo)
                                command.Parameters.AddWithValue("@arrivo", dataArrivo); // Data di arrivo della chiamata
                                command.Parameters.AddWithValue("@fine", dataArrivo); // Data di fine chiamata inizialmente uguale a dataArrivo
                                command.Parameters.AddWithValue("@rsChiamante", ragioneSocialeChiamante);
                                command.Parameters.AddWithValue("@rsChiamato", ragioneSocialeChiamato);
                                command.Parameters.AddWithValue("@uniqueid", uniqueId);
                                command.Parameters.AddWithValue("@locazione", locazione);

                                await command.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();
                            _logger.LogInformation($"4.2.1. Chiamata registrata: {numeroChiamante}, DataArrivo: {dataArrivo}");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError($"4.2.2. Errore durante la registrazione della chiamata: {ex.Message}");
                        }
                    }
                }
            }
            
            catch (Exception ex)
            {
                _logger.LogError($"4.2.3. Errore di connessione al database: {ex.Message}");
            }
        }

        public async Task UpdateCallEndTime(string numeroChiamante, DateTime startTime, DateTime endTime, string hangupUniqueId, string linkedId = null)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Cerchiamo la chiamata originale usando prima il linkedId se disponibile,
                            // altrimenti usiamo l'hangupUniqueId
                            string queryTrovaChiamata = @"
                        SELECT TOP 1 ID
                        FROM Chiamate
                        WHERE NumeroChiamante = @numeroChiamante
                        AND DataArrivoChiamata = @startTime
                        AND (UniqueId = @linkedId OR UniqueId = @hangupUniqueId)
                        AND DataFineChiamata IS NULL";

                            int idChiamata = 0;

                            using (var command = new SqlCommand(queryTrovaChiamata, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@numeroChiamante", numeroChiamante);
                                command.Parameters.AddWithValue("@startTime", startTime);
                                command.Parameters.AddWithValue("@hangupUniqueId", hangupUniqueId);
                                command.Parameters.AddWithValue("@linkedId", linkedId ?? hangupUniqueId); // Se linkedId è null, usa hangupUniqueId

                                using (var reader = await command.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        idChiamata = reader.GetInt32(0);
                                    }
                                }
                            }

                            if (idChiamata > 0)
                            {
                                // Aggiorna solo la data di fine chiamata
                                string queryAggiornaChiamata = @"
                            UPDATE Chiamate
                            SET DataFineChiamata = @endTime
                            WHERE ID = @id";

                                using (var command = new SqlCommand(queryAggiornaChiamata, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@endTime", endTime);
                                    command.Parameters.AddWithValue("@id", idChiamata);

                                    await command.ExecuteNonQueryAsync();
                                }

                                transaction.Commit();
                                _logger.LogInformation($"Chiamata aggiornata - Numero: {numeroChiamante}, " +
                                    $"LinkedId: {linkedId}, HangupUniqueId: {hangupUniqueId}, " +
                                    $"Fine chiamata: {endTime}");
                            }
                            else
                            {
                                _logger.LogWarning($"Nessuna chiamata attiva trovata per: " +
                                    $"Numero: {numeroChiamante}, " +
                                    $"Data inizio: {startTime}, " +
                                    $"LinkedId: {linkedId}, " +
                                    $"HangupUniqueId: {hangupUniqueId}");
                            }
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError($"Errore durante l'aggiornamento della chiamata: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore di connessione al database: {ex.Message}");
            }
        }

        public async Task<List<Chiamata>> GetChiamateByNumeroAsync(string numeroTelefono)
        {

            var chiamate = new List<Chiamata>();

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT c.ID, c.TipoChiamata, c.DataArrivoChiamata, c.DataFineChiamata,
                       r1.NumeroContatto AS NumeroChiamante, r2.NumeroContatto AS NumeroChiamato
                FROM Chiamate c
                INNER JOIN Rubrica r1 ON c.NumeroChiamanteID = r1.ID
                INNER JOIN Rubrica r2 ON c.NumeroChiamatoID = r2.ID
                WHERE r1.NumeroContatto = @numero OR r2.NumeroContatto = @numero
                ORDER BY c.DataArrivoChiamata DESC";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@numero", numeroTelefono);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                chiamate.Add(new Chiamata
                                {
                                    ID = reader.GetInt32(0),
                                    TipoChiamata = reader["TipoChiamata"].ToString(),
                                    DataArrivoChiamata = reader.GetDateTime(2),
                                    DataFineChiamata = reader.GetDateTime(3),
                                    NumeroChiamante = reader["NumeroChiamante"].ToString(),
                                    NumeroChiamato = reader["NumeroChiamato"].ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore nel recupero delle chiamate: {ex.Message}");
            }

            return chiamate;
        }

        private async Task<string> TrovaOInserisciNumeroAsync(SqlConnection connection, SqlTransaction transaction, string numero)
        {
            // Cerca il numero nella rubrica
            string queryCerca = "SELECT ID FROM Rubrica WHERE NumeroContatto = @numero";
            using (var command = new SqlCommand(queryCerca, connection, transaction))
            {
                command.Parameters.AddWithValue("@numero", numero);
                var result = await command.ExecuteScalarAsync();

                if (result != null)
                    return numero;
            }

            // Se non esiste, lo inserisce
            string queryInserisci = "INSERT INTO Rubrica (NumeroContatto) OUTPUT INSERTED.ID VALUES (@numero)";
            using (var command = new SqlCommand(queryInserisci, connection, transaction))
            {
                command.Parameters.AddWithValue("@numero", numero);
                return numero;
            }

        }

        public List<CallRecord> GetAllCalls()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                var query = "SELECT * FROM Chiamate"; // Assicurati che la tabella sia corretta
                return connection.Query<CallRecord>(query).ToList();
            }
        }

        public async Task<bool> AggiungiContattoAsync(string numeroContatto, string ragioneSociale, string citta, int? interno)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                IF EXISTS (SELECT 1 FROM Rubrica WHERE NumeroContatto = @numero)
                BEGIN
                    UPDATE Rubrica 
                    SET RagioneSociale = @ragioneSociale, 
                        CittaProvenienza = @citta,
                        Interno = @interno
                    WHERE NumeroContatto = @numero
                END
                ELSE
                BEGIN
                    INSERT INTO Rubrica (NumeroContatto, RagioneSociale, CittaProvenienza, Interno)
                    VALUES (@numero, @ragioneSociale, @citta, @interno)
                END";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@numero", numeroContatto);
                        command.Parameters.AddWithValue("@ragioneSociale", (object)ragioneSociale ?? DBNull.Value);
                        command.Parameters.AddWithValue("@citta", (object)citta ?? DBNull.Value);
                        command.Parameters.AddWithValue("@interno", (object)interno ?? DBNull.Value);

                        await command.ExecuteNonQueryAsync();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'aggiunta/aggiornamento del contatto: {ex.Message}");
                return false;
            }
        }

        public async Task<Chiamata> GetChiamataByIdAsync(int id)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT c.ID, c.TipoChiamata, c.DataArrivoChiamata, c.DataFineChiamata, c.Extra,
                       r1.NumeroContatto AS NumeroChiamante, r2.NumeroContatto AS NumeroChiamato
                FROM Chiamate c
                INNER JOIN Rubrica r1 ON c.NumeroChiamanteID = r1.ID
                INNER JOIN Rubrica r2 ON c.NumeroChiamatoID = r2.ID
                WHERE c.ID = @id";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@id", id);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return new Chiamata
                                {
                                    ID = reader.GetInt32(0),
                                    TipoChiamata = reader["TipoChiamata"].ToString(),
                                    DataArrivoChiamata = reader.GetDateTime(2),
                                    DataFineChiamata = reader.GetDateTime(3),
                                    Extra = reader["Extra"] != DBNull.Value ? reader["Extra"].ToString() : null,
                                    NumeroChiamante = reader["NumeroChiamante"].ToString(),
                                    NumeroChiamato = reader["NumeroChiamato"].ToString()
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore nel recupero della chiamata: {ex.Message}");
            }

            return null;
        }

        public async Task<bool> UpdateCallLocationAsync(int callId, string location)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Verifica prima che la chiamata esista
                            string queryVerifica = @"
                        SELECT COUNT(1) 
                        FROM Chiamate 
                        WHERE ID = @id";

                            bool exists = false;

                            using (var command = new SqlCommand(queryVerifica, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@id", callId);
                                exists = (int)await command.ExecuteScalarAsync() > 0;
                            }

                            if (exists)
                            {
                                // Aggiorna il campo Locazione
                                string queryAggiorna = @"
                            UPDATE Chiamate
                            SET Locazione = @location
                            WHERE ID = @id";

                                using (var command = new SqlCommand(queryAggiorna, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@location", location);
                                    command.Parameters.AddWithValue("@id", callId);

                                    int rowsAffected = await command.ExecuteNonQueryAsync();

                                    if (rowsAffected > 0)
                                    {
                                        transaction.Commit();
                                        _logger.LogInformation($"Locazione aggiornata per chiamata ID: {callId}, Nuova locazione: {location}");
                                        return true;
                                    }
                                }
                            }

                            _logger.LogWarning($"Nessuna chiamata trovata con ID: {callId}");
                            return false;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError($"Errore durante l'aggiornamento della locazione: {ex.Message}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore di connessione al database: {ex.Message}");
                return false;
            }
        }
    }

    public class Contatto
    {
        public string? RagioneSociale { get; set; }
        public string? Citta { get; set; }
        public string? NumeroContatto { get; set; }
        public int? Interno { get; set; }
    }

    public class Chiamata 
    {
        public int? ID { get; set; }

        public string? TipoChiamata { get; set; }

        public DateTime? DataArrivoChiamata { get; set; }

        public DateTime? DataFineChiamata { get; set; }

        public string? NumeroChiamante { get; set; }

        public string? NumeroChiamato { get; set; }

        // Campo da aggiornare col comune da dove sta chiamando il chiamante, dopo l'arrivo e la fine della chiamata.
        public string? Extra { get; set; }
    }

    public class CallRecord
    {
        public int ID { get; set; }
        public int NumeroChiamanteID { get; set; }
        public int NumeroChiamatoID { get; set; }
        public string? TipoChiamata { get; set; }
        public DateTime DataArrivoChiamata { get; set; }
        public DateTime DataFineChiamata { get; set; }

        // Campo da aggiornare col comune da dove sta chiamando il chiamante, dopo l'arrivo e la fine della chiamata.
        public string? Extra { get; set; }
    }
} 