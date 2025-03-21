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
                _logger.LogError($"Errore di connessione al database: {ex.Message}");
            }

            return null;
        }

        public async Task RegisterCall(string numeroChiamante, string ragioneSociale, double durata, DateTime starttime)
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
                            int idChiamante = await TrovaOInserisciNumeroAsync(connection, transaction, numeroChiamante);

                            // Trova o inserisce il numero del chiamato (puoi usare un valore predefinito o un altro numero)
                            int idChiamato = await TrovaOInserisciNumeroAsync(connection, transaction, "0000000000"); // Usa un valore predefinito o personalizzato

                            // Data di arrivo della chiamata
                            DateTime dataArrivo = starttime;

                            // Inserisce la chiamata nella tabella Chiamate
                            string queryInserisciChiamata = @"
                        INSERT INTO Chiamate (NumeroChiamanteID, NumeroChiamatoID, TipoChiamata, DataArrivoChiamata, DataFineChiamata)
                        VALUES (@chiamante, @chiamato, @tipo, @arrivo, @fine)";

                            using (var command = new SqlCommand(queryInserisciChiamata, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@chiamante", idChiamante);
                                command.Parameters.AddWithValue("@chiamato", idChiamato);
                                command.Parameters.AddWithValue("@tipo", "Entrata"); // Tipo di chiamata (puoi personalizzarlo)
                                command.Parameters.AddWithValue("@arrivo", dataArrivo); // Data di arrivo della chiamata
                                command.Parameters.AddWithValue("@fine", dataArrivo); // Data di fine chiamata inizialmente uguale a dataArrivo

                                await command.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();
                            _logger.LogInformation($"Chiamata registrata: {numeroChiamante}, DataArrivo: {dataArrivo}");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError($"Errore durante la registrazione della chiamata: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore di connessione al database: {ex.Message}");
            }
        }

        public async Task UpdateCallDuration(string numeroChiamante, double durata, DateTime endtime)
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
                            // Trova l'ID del chiamante nella Rubrica
                            int idChiamante = await TrovaOInserisciNumeroAsync(connection, transaction, numeroChiamante);

                            // Trova la chiamata più recente per il chiamante
                            string queryTrovaChiamata = @"
                        SELECT TOP 1 ID, DataArrivoChiamata
                        FROM Chiamate
                        WHERE NumeroChiamanteID = @chiamante
                        ORDER BY DataArrivoChiamata DESC";

                            int idChiamata = 0;
                            DateTime dataArrivo = endtime;

                            using (var command = new SqlCommand(queryTrovaChiamata, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@chiamante", idChiamante);

                                using (var reader = await command.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        idChiamata = reader.GetInt32(0);
                                        dataArrivo = reader.GetDateTime(1);
                                    }
                                }
                            }

                            if (idChiamata > 0)
                            {
                                // Calcola la data di fine chiamata
                                DateTime dataFine = dataArrivo.AddSeconds(durata);

                                // Aggiorna la data di fine chiamata
                                string queryAggiornaChiamata = @"
                            UPDATE Chiamate
                            SET DataFineChiamata = @fine
                            WHERE ID = @id";

                                using (var command = new SqlCommand(queryAggiornaChiamata, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@fine", dataFine);
                                    command.Parameters.AddWithValue("@id", idChiamata);

                                    await command.ExecuteNonQueryAsync();
                                }

                                transaction.Commit();
                                _logger.LogInformation($"Durata della chiamata aggiornata: {numeroChiamante}, Durata: {durata} secondi");
                            }
                            else
                            {
                                _logger.LogWarning($"Nessuna chiamata trovata per il numero: {numeroChiamante}");
                            }
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError($"Errore durante l'aggiornamento della durata della chiamata: {ex.Message}");
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

        public async Task<bool> AggiungiChiamataAsync(string numeroChiamante, string numeroChiamato, string tipoChiamata, DateTime dataArrivo, DateTime dataFine)
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
                            // Recupera o inserisce il chiamante
                            int idChiamante = await TrovaOInserisciNumeroAsync(connection, transaction, numeroChiamante);

                            // Recupera o inserisce il chiamato
                            int idChiamato = await TrovaOInserisciNumeroAsync(connection, transaction, numeroChiamato);

                            // Inserisce la chiamata
                            string queryInserisciChiamata = @"
                        INSERT INTO Chiamate (NumeroChiamanteID, NumeroChiamatoID, TipoChiamata, DataArrivoChiamata, DataFineChiamata)
                        VALUES (@chiamante, @chiamato, @tipo, @arrivo, @fine)";

                            using (var command = new SqlCommand(queryInserisciChiamata, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@chiamante", idChiamante);
                                command.Parameters.AddWithValue("@chiamato", idChiamato);
                                command.Parameters.AddWithValue("@tipo", tipoChiamata);
                                command.Parameters.AddWithValue("@arrivo", dataArrivo);
                                command.Parameters.AddWithValue("@fine", dataFine);

                                await command.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();
                            return true;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError($"Errore nell'inserimento della chiamata: {ex.Message}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore nella connessione al database: {ex.Message}");
                return false;
            }
        }

        private async Task<int> TrovaOInserisciNumeroAsync(SqlConnection connection, SqlTransaction transaction, string numero)
        {
            // Cerca il numero nella rubrica
            string queryCerca = "SELECT ID FROM Rubrica WHERE NumeroContatto = @numero";
            using (var command = new SqlCommand(queryCerca, connection, transaction))
            {
                command.Parameters.AddWithValue("@numero", numero);
                var result = await command.ExecuteScalarAsync();

                if (result != null)
                    return Convert.ToInt32(result);
            }

            // Se non esiste, lo inserisce
            string queryInserisci = "INSERT INTO Rubrica (NumeroContatto) OUTPUT INSERTED.ID VALUES (@numero)";
            using (var command = new SqlCommand(queryInserisci, connection, transaction))
            {
                command.Parameters.AddWithValue("@numero", numero);
                return (int)await command.ExecuteScalarAsync();
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
    }

    public class Contatto
    {
        public string? RagioneSociale { get; set; }
        public string? Citta { get; set; }
    }

    public class Chiamata 
    {
        public int? ID { get; set; }

        public string? TipoChiamata { get; set; }

        public DateTime? DataArrivoChiamata { get; set; }

        public DateTime? DataFineChiamata { get; set; }

        public string? NumeroChiamante { get; set; }

        public string? NumeroChiamato { get; set; }
    }

    public class CallRecord
    {
        public int ID { get; set; }
        public int NumeroChiamanteID { get; set; }
        public int NumeroChiamatoID { get; set; }
        public string? TipoChiamata { get; set; }
        public DateTime DataArrivoChiamata { get; set; }
        public DateTime DataFineChiamata { get; set; }
    }
} 