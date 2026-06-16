using Microsoft.AspNetCore.Http;

namespace AfbGenerator.Api.Models;

public class LibelleImportExcelRequest
{
    public IFormFile File { get; set; } = null!;
    public int? FluxId { get; set; }
}
