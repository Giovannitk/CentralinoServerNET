using Microsoft.AspNetCore.Mvc;
using ServerCentralino.Services;
using System.Linq;

namespace ServerCentralino.Controllers
{

    [Route("api/[controller]")]
    public class CallController : Controller
    {
        private readonly DatabaseService _callStatisticsService;
        private readonly ILogger<ServiceCall> _logger;

        public CallController(DatabaseService callStatisticsService, ILogger<ServiceCall> logger)
        {
            _callStatisticsService = callStatisticsService;
            _logger = logger;
        }

        [HttpPost("make-call")]
        public IActionResult MakeCall([FromBody] MakeCallRequest request)
        {
            if (string.IsNullOrEmpty(request.Channel) || string.IsNullOrEmpty(request.Exten) || string.IsNullOrEmpty(request.CallerId))
            {
                return BadRequest("I parametri Channel, Exten e CallerId sono obbligatori.");
            }

            try
            {
                //_serviceCall.MakeCall(request.Channel, request.Exten, request.CallerId);
                return Ok(new { Message = "Chiamata avviata con successo." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Errore durante l'avvio della chiamata.", Error = ex.Message });
            }
        }

        [HttpGet("get-all-calls")]
        public IActionResult GetAllCalls()
        {
            try
            {
                var calls = _callStatisticsService.GetAllCalls(); // Metodo che devi implementare in DatabaseService
                
                return Ok(calls);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Errore durante il recupero delle chiamate.", Error = ex.Message });
            }
        }

        [HttpGet("find-contact")]
        public async Task<IActionResult> FindContact(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
            {
                return BadRequest("Il numero di telefono è obbligatorio.");
            }

            try
            {
                var contact = await _callStatisticsService.CercaContattoAsync(phoneNumber);
                if (contact == null)
                {
                    return NotFound("Contatto non trovato.");
                }
                return Ok(contact);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Errore durante la ricerca del contatto.", Error = ex.Message });
            }
        }

        [HttpPost("add-contact")]
        public async Task<IActionResult> AddContact([FromBody] AddContactRequest request)
        {
            if (string.IsNullOrEmpty(request.NumeroContatto))
            {
                return BadRequest("Il numero di contatto è obbligatorio.");
            }

            try
            {
                // Implementare un metodo in DatabaseService per aggiungere un contatto
                bool success = await _callStatisticsService.AggiungiContattoAsync(
                    request.NumeroContatto,
                    request.RagioneSociale,
                    request.Citta,
                    request.Interno);

                if (success)
                {
                    return Ok(new { Message = "Contatto aggiunto con successo." });
                }
                else
                {
                    return StatusCode(500, new { Message = "Errore durante l'aggiunta del contatto." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Errore durante l'aggiunta del contatto.", Error = ex.Message });
            }
        }

        [HttpGet("find-call")]
        public async Task<IActionResult> FindCall(int callId)
        {
            if (callId <= 0)
            {
                return BadRequest("L'ID della chiamata deve essere un numero positivo.");
            }

            try
            {
                // Implementare un metodo in DatabaseService per cercare una chiamata per ID
                var call = await _callStatisticsService.GetChiamataByIdAsync(callId);
                if (call == null)
                {
                    return NotFound("Chiamata non trovata.");
                }
                return Ok(call);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Errore durante la ricerca della chiamata.", Error = ex.Message });
            }
        }

        [HttpPut("update-call-location")]
        public async Task<IActionResult> UpdateCallLocation([FromBody] UpdateCallLocationRequest request)
        {
            if (request.CallId <= 0)
            {
                return BadRequest("L'ID della chiamata deve essere un numero positivo.");
            }

            if (string.IsNullOrEmpty(request.Location))
            {
                return BadRequest("La località è obbligatoria.");
            }

            try
            {
                
                bool success = await _callStatisticsService.UpdateCallLocationAsync(request.CallId, request.Location);

                if (success)
                {
                    return Ok(new { Message = "Località della chiamata aggiornata con successo." });
                }
                else
                {
                    return StatusCode(500, new { Message = "Errore durante l'aggiornamento della località." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Errore durante l'aggiornamento della località.", Error = ex.Message });
            }
        }

        [HttpGet("get-calls-by-number")]
        public async Task<IActionResult> GetCallsByNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
            {
                return BadRequest("Il numero di telefono è obbligatorio.");
            }

            try
            {
                var calls = await _callStatisticsService.GetChiamateByNumeroAsync(phoneNumber);
                return Ok(calls);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Errore durante il recupero delle chiamate.", Error = ex.Message });
            }
        }
    }

    public class MakeCallRequest
    {
        public string? Channel { get; set; }
        public string? Exten { get; set; }
        public string? CallerId { get; set; }
    }

    public class AddContactRequest
    {
        public string? NumeroContatto { get; set; }
        public string? RagioneSociale { get; set; }
        public string? Citta { get; set; }
        public int? Interno { get; set; }
    }

    public class UpdateCallLocationRequest
    {
        public int CallId { get; set; }
        public string? Location { get; set; }
    }
}
