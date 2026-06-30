using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AfbGenerator.Api.Data;
using AfbGenerator.Api.Entities;
using AfbGenerator.Api.Services;
using AfbGenerator.Api.Models;
using System.Text;

namespace AfbGenerator.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BankFileController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly BankFileFormatService _formatService;
        private readonly LibelleService _libelleService;
        public BankFileController(AppDbContext context, BankFileFormatService formatService ,LibelleService lib)
        {
            _context = context;
            _formatService = formatService;
            _libelleService=lib;
        }

        // 1. GET: api/BankFile
        [HttpGet]
        public async Task<ActionResult<IEnumerable<BankFileFormat>>> GetAllFormats(CancellationToken ct)
        {
            var formats = await _context.BankFileFormats.ToListAsync(ct);
            return Ok(formats);
        }

        // 2. POST: api/BankFile
        [HttpPost]
        public async Task<ActionResult<BankFileFormat>> AddFormat(
            [FromBody] BankFileFormat newFormat, 
            CancellationToken ct)
        {
            if (newFormat == null) return BadRequest("Données invalides.");

            _context.BankFileFormats.Add(newFormat);
            await _context.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetAllFormats), new { id = newFormat.Id }, newFormat);
        }

        // 3. POST: api/BankFile/detect
 

[HttpPost("9dim")]
public async Task<IActionResult> DetectFileFormat(IFormFile file, CancellationToken ct)
{
    if (file == null || file.Length == 0)
        return BadRequest("Veuillez fournir un fichier valide.");

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

    List<string> rawLines;

    if (extension is ".xlsx" or ".xls")
    {
        rawLines = BuildRawLinesFromExcel(file);
    }
    else
    {
        rawLines = new List<string>();
        
        // CORRECTION 1 : Enregistrement du fournisseur d'encodage pour gérer les formats Excel/ANSI (Windows-1252)
        System.Text.EncodingProvider provider = System.Text.CodePagesEncodingProvider.Instance;
        System.Text.Encoding.RegisterProvider(provider);
        
        // Utilisation d'un StreamReader adaptatif capable de basculer sur l'encodage par défaut du système si ce n'est pas de l'UTF-8
        using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.GetEncoding("iso-8859-1"), true);
        
        int lineCount = 0;
        while (!reader.EndOfStream && lineCount < 20)
        {
            var line = await reader.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line))
            {
                // CORRECTION 2 : Nettoyage drastique des résidus de lignes (\r) et des espaces
                var cleanedLine = line.Replace("\r", "").Replace("\n", "").Trim();
                rawLines.Add(cleanedLine);
                lineCount++;
            }
        }
    }

    // 2. Détecter le format
    var matchedFormat = await _formatService.DetectFormatAsync(rawLines, ct);
    if (matchedFormat == null)
        return NotFound(new { Message = "Format de fichier bancaire inconnu." });

    // 3. Extraire les headers
    var headerLine = rawLines.FirstOrDefault(line =>
        line.Contains(matchedFormat.HeaderPattern?.Trim(), StringComparison.OrdinalIgnoreCase));

    var headers = new List<string>();
    if (!string.IsNullOrEmpty(headerLine))
    {
        var sep = extension is ".xlsx" or ".xls" ? "\t" : matchedFormat.Separator;
        headers = headerLine
            .Split(new[] { sep }, StringSplitOptions.None)
            .Select(h => h.Trim())
            .ToList();
    }

    return Ok(new { Format = matchedFormat, DetectedHeaders = headers });
}
// ── Helper : lit les premières lignes d'un Excel et les convertit en strings TAB-séparées
private List<string> BuildRawLinesFromExcel(IFormFile file)
{
    var lines = new List<string>();

    // Réinitialiser le stream au cas où il aurait déjà été lu
    file.OpenReadStream().Seek(0, SeekOrigin.Begin);

    // Utiliser ReadWorksheetRows que tu as déjà dans LibelleService
    var rows = _libelleService.ReadWorksheetRows(file);

    foreach (var row in rows.Take(20))
    {
        // Joindre toutes les cellules avec TAB → ligne comparable à du texte
        var line = string.Join("\t", row.Select(c => c?.Trim() ?? ""));
        if (!string.IsNullOrWhiteSpace(line))
            lines.Add(line);
    }

    return lines;
}
    }
}