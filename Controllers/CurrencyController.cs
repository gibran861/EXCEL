using System.Collections.Generic;
using System.Threading.Tasks;
using AfbGenerator.Api.Models;
using AfbGenerator.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using AfbGenerator.Api.Entities.Xrt;

namespace AfbGenerator.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CurrencyController : ControllerBase
    {
        private readonly CurrencyService _currencyService;

        public CurrencyController(CurrencyService currencyService)
        {
            _currencyService = currencyService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<GS_CUR>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<GS_CUR>>> GetAll(CancellationToken cancellationToken)
        {
            var currencies = await _currencyService.GetAllAsync(cancellationToken);
            return Ok(currencies);
        }

        [HttpGet("{curId}/decimals")]
        [ProducesResponseType(typeof(byte), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<byte>> GetDecimals(string curId, CancellationToken cancellationToken)
        {
            try
            {
                var decimals = await _currencyService.GetDecimalsByCurIdAsync(curId, cancellationToken);
                return Ok(decimals);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}