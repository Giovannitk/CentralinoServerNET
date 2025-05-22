using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace ServerCentralino.Services
{
    public class DatabaseService
    {
        private readonly string? _connectionString;
        private readonly ILogger<DatabaseService> _logger;

        private string DecryptPasswordInConnectionString(string connectionString)
        {
            var builder = new System.Data.SqlClient.SqlConnectionStringBuilder(connectionString);

            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = CryptoHelper.Decrypt(builder.Password);
            }

            return builder.ConnectionString;
        }

        public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
        {
            _logger = logger;

            var rawConnectionString = configuration.GetConnectionString("DefaultConnection");
            _connectionString = DecryptPasswordInConnectionString(rawConnectionString);

            // Test immediato della connessione
            try
            {
                using var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
                connection.Open();

                using var command = new Microsoft.Data.SqlClient.SqlCommand("SELECT 1", connection);
                command.ExecuteScalar(); // Lancia eccezione se le credenziali non sono valide

                _logger.LogInformation("Connessione al database riuscita.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore nella connessione al database: {ex.Message}");
                MessageBox.Show($"Errore nella connessione al database: {ex.Message}", "Errore critico", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown(); // Chiude l'app
            }
        }


        public async Task<Contatto?> CercaContattoAsync(string numeroTelefono)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT RagioneSociale, CittaProvenienza, Interno
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
                                    Citta = reader["CittaProvenienza"].ToString(),
                                    Interno = reader["Interno"] != DBNull.Value ? Convert.ToInt32(reader["Interno"]) : 0
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

        public async Task RegisterCall(string numeroChiamante, string numeroChiamato, string ragioneSocialeChiamante, string ragioneSocialeChiamato, DateTime starttime, string tipoChiamata, string uniqueId, string locazione, string? campoExtra1 = null)
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
                            // Trovo o inserisco il numero del chiamante nella Rubrica
                            string idChiamante = await TrovaOInserisciNumeroAsync(connection, transaction, numeroChiamante);

                            // Trovo o inserisco il numero del chiamato (può essere usato un valore predefinito o un altro numero)
                            string idChiamato = "0000000000"; //await TrovaOInserisciNumeroAsync(connection, transaction, numeroChiamato); // Usa un valore predefinito o personalizzato

                            // Data di arrivo della chiamata
                            DateTime dataArrivo = starttime;

                            // Inserisce la chiamata nella tabella Chiamate
                            string queryInserisciChiamata = @"
                        INSERT INTO Chiamate (NumeroChiamante, NumeroChiamato, TipoChiamata, DataArrivoChiamata, DataFineChiamata, RagioneSocialeChiamante, RagioneSocialeChiamato, UniqueId, Locazione, CampoExtra1)
                        VALUES (@chiamante, @chiamato, @tipo, @arrivo, @fine, @rsChiamante, @rsChiamato, @uniqueid, @locazione, @campoExtra1)";

                            using (var command = new Microsoft.Data.SqlClient.SqlCommand(queryInserisciChiamata, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@chiamante", idChiamante);
                                command.Parameters.AddWithValue("@chiamato", idChiamato);
                                command.Parameters.AddWithValue("@tipo", tipoChiamata); // Tipo di chiamata 
                                command.Parameters.AddWithValue("@arrivo", dataArrivo); // Data di arrivo della chiamata
                                command.Parameters.AddWithValue("@fine", dataArrivo); // Data di fine chiamata inizialmente uguale a dataArrivo
                                command.Parameters.AddWithValue("@rsChiamante", ragioneSocialeChiamante);
                                command.Parameters.AddWithValue("@rsChiamato", ragioneSocialeChiamato);
                                command.Parameters.AddWithValue("@uniqueid", uniqueId);
                                command.Parameters.AddWithValue("@locazione", locazione);
                                command.Parameters.AddWithValue("@campoExtra1", (object)campoExtra1 ?? DBNull.Value);

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
            // Cerco il numero nella rubrica
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
                SELECT 
                    NumeroChiamante, 
                    NumeroChiamato,
                    RagioneSocialeChiamante,
                    RagioneSocialeChiamato,
                    DataArrivoChiamata,
                    DataFineChiamata,
                    TipoChiamata,
                    Locazione,
                    UniqueID
                FROM Chiamate
                WHERE ID = @id";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@id", id);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return new Chiamata
                                {
                                    // Nota: gli indici partono da 0 ora che abbiamo rimosso ID dalla SELECT
                                    NumeroChiamante = reader.IsDBNull(0) ? null : reader.GetString(0),
                                    NumeroChiamato = reader.IsDBNull(1) ? null : reader.GetString(1),
                                    RagioneSocialeChiamante = reader.IsDBNull(2) ? null : reader.GetString(2),
                                    RagioneSocialeChiamato = reader.IsDBNull(3) ? null : reader.GetString(3),
                                    DataArrivoChiamata = reader.GetDateTime(4),
                                    DataFineChiamata = reader.GetDateTime(5),
                                    TipoChiamata = reader.IsDBNull(6) ? null : reader.GetString(6),
                                    Locazione = reader.IsDBNull(7) ? null : reader.GetString(7),
                                    UniqueID = reader.IsDBNull(8) ? null : reader.GetString(8)
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore nel recupero della chiamata: {ex.Message}");
                throw;
            }

            return null;
        }


        // Aggiungi chiamata
        public async Task<bool> AggiungiChiamataAsync(Chiamata chiamata)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
            INSERT INTO Chiamate 
            (NumeroChiamante, NumeroChiamato, TipoChiamata, DataArrivoChiamata, DataFineChiamata, UniqueID, Locazione, RagioneSocialeChiamante, RagioneSocialeChiamato, CampoExtra1)
            VALUES 
            (@NumeroChiamante, @NumeroChiamato, @TipoChiamata, @DataArrivoChiamata, @DataFineChiamata, @UniqueID, @Locazione, @RagioneSocialeChiamante, @RagioneSocialeChiamato, @CampoExtra1)";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@NumeroChiamante", chiamata.NumeroChiamante);
                        command.Parameters.AddWithValue("@NumeroChiamato", chiamata.NumeroChiamato);
                        command.Parameters.AddWithValue("@TipoChiamata", chiamata.TipoChiamata);
                        command.Parameters.AddWithValue("@DataArrivoChiamata", chiamata.DataArrivoChiamata);
                        command.Parameters.AddWithValue("@DataFineChiamata", chiamata.DataFineChiamata);
                        command.Parameters.AddWithValue("@UniqueID", chiamata.UniqueID ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Locazione", chiamata.Locazione ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@RagioneSocialeChiamante", chiamata.RagioneSocialeChiamante ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@RagioneSocialeChiamato", chiamata.RagioneSocialeChiamato ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@CampoExtra1", chiamata.CampoExtra1 ?? (object)DBNull.Value);

                        int rows = await command.ExecuteNonQueryAsync();
                        return rows > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'inserimento della chiamata: {ex.Message}");
                return false;
            }
        }


        // Aggiorna Chiamata
        public async Task<bool> AggiornaChiamataAsync(Chiamata chiamata)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
            UPDATE Chiamate
            SET 
                NumeroChiamante = @NumeroChiamante,
                NumeroChiamato = @NumeroChiamato,
                RagioneSocialeChiamante = @RagioneSocialeChiamante,
                RagioneSocialeChiamato = @RagioneSocialeChiamato,
                TipoChiamata = @TipoChiamata,
                DataArrivoChiamata = @DataArrivoChiamata,
                DataFineChiamata = @DataFineChiamata,
                UniqueID = @UniqueID,
                Locazione = @Locazione,
                CampoExtra1 = @CampoExtra1
            WHERE ID = @ID";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@NumeroChiamante", chiamata.NumeroChiamante ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@NumeroChiamato", chiamata.NumeroChiamato ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@RagioneSocialeChiamante", chiamata.RagioneSocialeChiamante ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@RagioneSocialeChiamato", chiamata.RagioneSocialeChiamato ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@TipoChiamata", chiamata.TipoChiamata ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@DataArrivoChiamata", chiamata.DataArrivoChiamata);
                        command.Parameters.AddWithValue("@DataFineChiamata", chiamata.DataFineChiamata);
                        command.Parameters.AddWithValue("@UniqueID", chiamata.UniqueID ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Locazione", chiamata.Locazione ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@CampoExtra1", chiamata.CampoExtra1 ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@ID", chiamata.Id);

                        int rows = await command.ExecuteNonQueryAsync();
                        return rows > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'aggiornamento della chiamata con ID {chiamata.Id}: {ex.Message}");
                return false;
            }
        }




        // Elimina Chiamata
        public async Task<bool> DeleteChiamataByIdAsync(int id)
        {
            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = "DELETE FROM Chiamate WHERE ID = @id";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@id", id);

                        int rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            _logger.LogInformation($"Chiamata eliminata con successo. ID: {id}");
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning($"Nessuna chiamata trovata con ID: {id}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'eliminazione della chiamata con ID {id}: {ex.Message}");
                return false;
            }
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
                            // Verifico prima che la chiamata esista
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
                                // Aggiorno il campo Locazione
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
                return false; // In caso di errore, si procede comunque
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


        public async Task<List<Contatto>> GetAllContattiAsync()
        {
            var contatti = new List<Contatto>();

            try
            {
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT NumeroContatto, RagioneSociale, CittaProvenienza, Interno
                FROM Rubrica";

                    using (var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            contatti.Add(new Contatto
                            {
                                NumeroContatto = reader["NumeroContatto"].ToString(),
                                RagioneSociale = reader["RagioneSociale"].ToString(),
                                Citta = reader["CittaProvenienza"].ToString(),
                                Interno = reader["Interno"] != DBNull.Value ? Convert.ToInt32(reader["Interno"]) : (int?)null
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante il recupero dei contatti: {ex.Message}");
            }

            return contatti;
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
        public string? CampoExtra1 { get; set; }
    }
} 