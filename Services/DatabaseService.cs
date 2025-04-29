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
            //var db_server = Environment.GetEnvironmentVariable("DB_SERVER");
            //var db_name = Environment.GetEnvironmentVariable("DB_NAME");
            //var db_user = Environment.GetEnvironmentVariable("DB_USER");
            //var db_password = Environment.GetEnvironmentVariable("DB_PASSWORD");
            //_connectionString = $"Server={db_server};Database={db_name};User Id={db_user};Password={db_password};TrustServerCertificate=True";

            _connectionString = configuration.GetConnectionString("DefaultConnection");
            Console.WriteLine($"{_connectionString}");
            _logger = logger;
        }


        public async Task<Contatto?> CercaContattoAsync(string numeroTelefono)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT RagioneSociale, CittaProvenienza
                FROM Rubrica
                WHERE NumeroContatto = @numero";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
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
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
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

                            using (var command = new Microsoft.Data.SqlClient.SqlCommand(queryInserisciChiamata, connection, transaction))
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

        public async Task<bool> UpdateCallEndTimeAsync(
    string linkedId,
    DateTime endTime,
    DateTime? startTime = null,
    string? callerNumber = null,
    string? ragioneSocialeChiamato = null)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                UPDATE Chiamate
                SET DataFineChiamata = @endTime";

                    if (!string.IsNullOrEmpty(callerNumber))
                    {
                        query += ", NumeroChiamato = @callerNumber";
                    }

                    if (!string.IsNullOrEmpty(ragioneSocialeChiamato))
                    {
                        query += ", RagioneSocialeChiamato = @ragioneSocialeChiamato";
                    }

                    query += " WHERE UniqueID = @linkedId AND DataFineChiamata = DataArrivoChiamata";

                    if (!string.IsNullOrEmpty(callerNumber))
                    {
                        query += " AND NumeroChiamante != @callerNumber";
                    }

                    //if (startTime.HasValue)
                    //{
                    //    query += " AND DataArrivoChiamata = @startTime";
                    //}

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@endTime", endTime);
                        command.Parameters.AddWithValue("@linkedId", linkedId);

                        if (!string.IsNullOrEmpty(callerNumber))
                        {
                            command.Parameters.AddWithValue("@callerNumber", callerNumber);
                        }

                        if (!string.IsNullOrEmpty(ragioneSocialeChiamato))
                        {
                            command.Parameters.AddWithValue("@ragioneSocialeChiamato", ragioneSocialeChiamato);
                        }

                        if (startTime.HasValue)
                        {
                            command.Parameters.AddWithValue("@startTime", startTime.Value);
                        }

                        int rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            _logger.LogInformation($"H: Chiamata {linkedId} aggiornata alle {endTime}. Numero chiamato: {callerNumber} - RS:{ragioneSocialeChiamato}");
                            return true;
                        }

                        _logger.LogWarning($"H: Chiamata {linkedId} non aggiornata (già terminata o non trovata)");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"H: Errore aggiornamento fine chiamata {linkedId}: {ex.Message}");
                return false;
            }
        }

        public async Task<List<Chiamata>> GetChiamateByNumeroAsync(string numeroTelefono)
        {
            var chiamate = new List<Chiamata>();

            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
        SELECT 
            NumeroChiamante, 
            NumeroChiamato, 
            RagioneSocialeChiamante, 
            RagioneSocialeChiamato, 
            DataArrivoChiamata, 
            DataFineChiamata, 
            TipoChiamata, 
            Locazione
        FROM Chiamate
        WHERE NumeroChiamante = @numero OR NumeroChiamato = @numero
        ORDER BY DataArrivoChiamata DESC";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@numero", numeroTelefono);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                chiamate.Add(new Chiamata
                                {
                                    NumeroChiamante = reader["NumeroChiamante"].ToString(),
                                    NumeroChiamato = reader["NumeroChiamato"].ToString(),
                                    RagioneSocialeChiamante = reader["RagioneSocialeChiamante"].ToString(),
                                    RagioneSocialeChiamato = reader["RagioneSocialeChiamato"].ToString(),
                                    DataArrivoChiamata = DateTime.Parse(reader["DataArrivoChiamata"].ToString()),
                                    DataFineChiamata = DateTime.Parse(reader["DataFineChiamata"].ToString()),
                                    TipoChiamata = reader["TipoChiamata"].ToString(),
                                    Locazione = reader["Locazione"].ToString()
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

        private async Task<string> TrovaOInserisciNumeroAsync(Microsoft.Data.SqlClient.SqlConnection connection, Microsoft.Data.SqlClient.SqlTransaction transaction, string numero)
        {
            // Cerca il numero nella rubrica
            string queryCerca = "SELECT ID FROM Rubrica WHERE NumeroContatto = @numero";
            using (var command = new Microsoft.Data.SqlClient.SqlCommand(queryCerca, connection, transaction))
            {
                command.Parameters.AddWithValue("@numero", numero);
                var result = await command.ExecuteScalarAsync();

                if (result != null)
                    return numero;
            }

            // Se non esiste, lo inserisce
            string queryInserisci = "INSERT INTO Rubrica (NumeroContatto) OUTPUT INSERTED.ID VALUES (@numero)";
            using (var command = new Microsoft.Data.SqlClient.SqlCommand(queryInserisci, connection, transaction))
            {
                command.Parameters.AddWithValue("@numero", numero);
                return numero;
            }

        }

        public List<Chiamata> GetAllCalls()
        {
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
            {
                connection.Open();
                var query = "SELECT * FROM Chiamate"; // Assicurati che la tabella sia corretta
                return connection.Query<Chiamata>(query).ToList();
            }
        }

        public async Task<bool> AggiungiContattoAsync(string numeroContatto, string ragioneSociale, string citta, int? interno)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
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

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
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
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT c.ID, c.TipoChiamata, c.DataArrivoChiamata, c.DataFineChiamata, c.Extra,
                       r1.NumeroContatto AS NumeroChiamante, r2.NumeroContatto AS NumeroChiamato
                FROM Chiamate c
                INNER JOIN Rubrica r1 ON c.NumeroChiamanteID = r1.ID
                INNER JOIN Rubrica r2 ON c.NumeroChiamatoID = r2.ID
                WHERE c.ID = @id";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@id", id);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return new Chiamata
                                {
                                    //ID = reader.GetInt32(0),
                                    TipoChiamata = reader["TipoChiamata"].ToString(),
                                    DataArrivoChiamata = reader.GetDateTime(2),
                                    DataFineChiamata = reader.GetDateTime(3),
                                    //Extra = reader["Extra"] != DBNull.Value ? reader["Extra"].ToString() : null,
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

        public async Task<Chiamata> GetChiamataByNumbers(string callerNumber, string calledNumber, DateTime endCall)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT TOP 1 
                    c.ID, c.TipoChiamata, c.DataArrivoChiamata, c.DataFineChiamata, 
                    c.Extra, c.UniqueID,
                    r1.NumeroContatto AS NumeroChiamante, 
                    r2.NumeroContatto AS NumeroChiamato
                FROM Chiamate c
                INNER JOIN Rubrica r1 ON c.NumeroChiamanteID = r1.ID
                INNER JOIN Rubrica r2 ON c.NumeroChiamatoID = r2.ID
                WHERE r1.NumeroContatto = @callerNumber 
                AND r2.NumeroContatto = @calledNumber
                ORDER BY c.DataFineChiamata DESC";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@callerNumber", callerNumber);
                        command.Parameters.AddWithValue("@calledNumber", calledNumber);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return new Chiamata
                                {
                                    //ID = reader.GetInt32(0),
                                    TipoChiamata = reader["TipoChiamata"].ToString(),
                                    DataArrivoChiamata = reader.GetDateTime(2),
                                    DataFineChiamata = reader.GetDateTime(3),
                                    // Extra = reader["Extra"] != DBNull.Value ? reader["Extra"].ToString() : null,
                                    NumeroChiamante = reader["NumeroChiamante"].ToString(),
                                    NumeroChiamato = reader["NumeroChiamato"].ToString(),
                                    UniqueID = reader["UniqueID"].ToString()
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
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
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

                            using (var command = new Microsoft.Data.SqlClient.SqlCommand(queryVerifica, connection, transaction))
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

                                using (var command = new Microsoft.Data.SqlClient.SqlCommand(queryAggiorna, connection, transaction))
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

        public async Task<bool> UpdateCalledNumberAsync(string linkedId, string calledNumber, string calledName)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                UPDATE Chiamate
                SET 
                    NumeroChiamato = @calledNumber,
                    RagioneSocialeChiamato = @calledName
                WHERE UniqueID = @linkedId";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@calledNumber", calledNumber);
                        command.Parameters.AddWithValue("@calledName", calledName);
                        command.Parameters.AddWithValue("@linkedId", linkedId);

                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'aggiornamento del numero chiamato: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CheckExistingCallAsync(string callKey)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT COUNT(1) 
                FROM Chiamate 
                WHERE UniqueID = @callKey
                AND DataArrivoChiamata >= DATEADD(minute, -5, GETDATE())";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@callKey", callKey);
                        int count = (int)await command.ExecuteScalarAsync();
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante la verifica della chiamata esistente: {ex.Message}");
                return false; // In caso di errore, procedi comunque
            }
        }

        public async Task<List<Contatto>> GetContattiIncompletiAsync()
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            var sql = "SELECT * FROM Rubrica WHERE RagioneSociale IS NULL OR CittaProvenienza IS NULL";

            return (await conn.QueryAsync<Contatto>(sql)).ToList();
        }

        public async Task<bool> DeleteContactAsync(string phoneNumber)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = "DELETE FROM Rubrica WHERE NumeroContatto = @numero";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@numero", phoneNumber);

                        int rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            _logger.LogInformation($"Contatto eliminato con successo: {phoneNumber}");
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning($"Nessun contatto trovato da eliminare per il numero: {phoneNumber}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'eliminazione del contatto {phoneNumber}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteChiamataByUniqueIdAsync(string uniqueId)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = "DELETE FROM Chiamate WHERE UniqueID = @uniqueId";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@uniqueId", uniqueId);

                        int rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            _logger.LogInformation($"Chiamata eliminata con successo. UniqueID: {uniqueId}");
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning($"Nessuna chiamata trovata con l'UniqueID: {uniqueId}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'eliminazione della chiamata con UniqueID {uniqueId}: {ex.Message}");
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
        public int Id { get; set; }
        public string? NumeroChiamante { get; set; }
        public string? NumeroChiamato { get; set; }
        public string? RagioneSocialeChiamante { get; set; }
        public string? RagioneSocialeChiamato { get; set; }
        public DateTime DataArrivoChiamata { get; set; }
        public DateTime DataFineChiamata { get; set; }
        public string? TipoChiamata { get; set; }
        public string? Locazione { get; set; }
        public string? UniqueID { get; set; }
    }
} 