using Microsoft.AspNetCore.Mvc;
using ServerCentralino.Services;
using System.Linq;

namespace ServerCentralino.Controllers
{
    [Route("api/[controller]")]
    public class CallController : Controller
    {
        private readonly DatabaseService _callStatisticsService;

        public CallController(DatabaseService callStatisticsService)
        {
            _callStatisticsService = callStatisticsService;
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
    }

    public class MakeCallRequest
    {
        public string? Channel { get; set; }
        public string? Exten { get; set; }
        public string? CallerId { get; set; }
    }
}
