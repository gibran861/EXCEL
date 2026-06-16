using Microsoft.AspNetCore.Http;

namespace AfbGenerator.Api.Models;

public class ExcelExtractRequest
{
    public IFormFile File { get; set; } = null!;
    public int? FluxId { get; set; }
}
