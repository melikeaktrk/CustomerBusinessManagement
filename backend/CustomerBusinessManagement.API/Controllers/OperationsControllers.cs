using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomerBusinessManagement.Business;
using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.DTO;
using CustomerBusinessManagement.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CustomerBusinessManagement.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CashTransactionsController(
    ApplicationDbContext db,
    ManagementService svc,
    IAuditLogService audit
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(
            await db
                .CashTransactions.AsNoTracking()
                .Where(x => !x.IsCancelled)
                .OrderByDescending(x => x.TransactionDate)
                .ToListAsync()
        );

    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Ok(await svc.CashSummary());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) =>
        await db.CashTransactions.FindAsync(id) is { } x ? Ok(x) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(CashRequest r)
    {
        if (r.Amount <= 0 || !Enum.TryParse<CashType>(r.TransactionType, true, out var t))
            return BadRequest();
        if (t == CashType.CashOut && (await svc.CashSummary()).CurrentBalance < r.Amount)
            return BadRequest(new { message = "Yetersiz kasa bakiyesi." });
        var x = new CashTransaction
        {
            TransactionType = t,
            Amount = r.Amount,
            Description = r.Description,
            TransactionDate = r.TransactionDate,
            ReferenceNumber = r.ReferenceNumber,
        };
        db.Add(x);
        await db.SaveChangesAsync();
        await audit.RecordAsync(
            "Create",
            x.GetType().Name,
            x.Id.ToString(),
            $"{x.GetType().Name} created"
        );
        return CreatedAtAction(nameof(Get), new { id = x.Id }, x);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CashRequest r)
    {
        var x = await db.CashTransactions.FindAsync(id);
        if (x == null)
            return NotFound();
        if (r.Amount <= 0 || !Enum.TryParse<CashType>(r.TransactionType, true, out var t))
            return BadRequest();
        // Düzenlenen kaydı mevcut bakiyeden çıkarıp yeni çıkışı kalan bakiye ile doğrula.
        var availableBalance = await db.CashTransactions
            .Where(row => row.Id != id && !row.IsCancelled)
            .SumAsync(row => row.TransactionType == CashType.CashIn ? row.Amount : -row.Amount);
        if (t == CashType.CashOut && availableBalance < r.Amount)
            return BadRequest(new { message = "Yetersiz kasa bakiyesi." });
        var oldDescription = x.Description;
        x.TransactionType = t;
        x.Amount = r.Amount;
        x.Description = r.Description;
        x.TransactionDate = r.TransactionDate;
        x.ReferenceNumber = r.ReferenceNumber;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Update", nameof(CashTransaction), id.ToString(), $"Kasa hareketi güncellendi: {oldDescription} → {x.Description}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var x = await db.CashTransactions.FindAsync(id);
        if (x == null)
            return NotFound();
        x.IsCancelled = true;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Delete", nameof(CashTransaction), id.ToString(), $"Kasa hareketi iptal edildi: {x.Description}");
        return NoContent();
    }
}

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ZReportsController(
    ApplicationDbContext db,
    IZReportOcrService ocr,
    IAuditLogService audit,
    Microsoft.AspNetCore.Hosting.IWebHostEnvironment? environment = null
) : ControllerBase
{
    private string DocumentDirectory => Path.Combine(environment?.ContentRootPath ?? AppContext.BaseDirectory, "App_Data", "ZReports");
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.ZReports.AsNoTracking().OrderByDescending(x => x.ReportDate).ToListAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) =>
        await db.ZReports.FindAsync(id) is { } x ? Ok(x) : NotFound();

    [HttpGet("{id:int}/document")]
    public async Task<IActionResult> Document(int id)
    {
        var report = await db.ZReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (report?.DocumentPath is not { Length: > 0 } path)
            return NotFound();
        var safeName = Path.GetFileName(path);
        var fullPath = Path.GetFullPath(Path.Combine(DocumentDirectory, safeName));
        var root = Path.GetFullPath(DocumentDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(fullPath))
            return NotFound();
        var contentType = Path.GetExtension(safeName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };
        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ZReportRequest r)
    {
        if (!r.IsConfirmed)
            return BadRequest(new { message = "Z raporu admin tarafından onaylanmalıdır." });
        var x = Map(r);
        db.Add(x);
        await db.SaveChangesAsync();
        await audit.RecordAsync(
            "Create",
            x.GetType().Name,
            x.Id.ToString(),
            $"{x.GetType().Name} created"
        );
        return CreatedAtAction(nameof(Get), new { id = x.Id }, x);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ZReportRequest r)
    {
        var x = await db.ZReports.FindAsync(id);
        if (x == null)
            return NotFound();
        if (!r.IsConfirmed)
            return BadRequest();
        x.ReportDate = r.ReportDate;
        x.GrossSales = r.GrossSales;
        x.TotalVat = r.TotalVat;
        x.NetSales = r.NetSales;
        x.CashAmount = r.CashAmount;
        x.CardAmount = r.CardAmount;
        x.TotalAmount = r.TotalAmount;
        x.DocumentPath = r.DocumentPath;
        x.OcrRawText = r.OcrRawText;
        x.IsConfirmed = true;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Update", nameof(ZReport), id.ToString(), "Z raporu güncellendi.");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var x = await db.ZReports.FindAsync(id);
        if (x == null)
            return NotFound();
        db.Remove(x);
        await db.SaveChangesAsync();
        await audit.RecordAsync("Delete", nameof(ZReport), id.ToString(), "Z raporu silindi.");
        return NoContent();
    }

    [HttpPost("ocr")]
    public async Task<IActionResult> Ocr(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "Boş dosya yüklenemez; en fazla 10 MB dosya seçin." });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var expectedMime = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".pdf" => "application/pdf",
            _ => null,
        };
        if (expectedMime is null || !string.Equals(file.ContentType, expectedMime, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Dosya uzantısı ve MIME türü eşleşmiyor. PNG, JPG veya PDF yükleyin." });
        if (!await HasValidSignatureAsync(file, ext, ct))
            return BadRequest(new { message = "Dosya içeriği seçilen dosya türüyle eşleşmiyor." });

        // OCR başarısız olsa bile doğrulanmış dosyayı sakla; kullanıcı gerçek değerleri elle girebilir.
        Directory.CreateDirectory(DocumentDirectory);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var storedPath = Path.Combine(DocumentDirectory, storedName);
        await using (var output = System.IO.File.Create(storedPath))
            await file.CopyToAsync(output, ct);

        OcrResult result;
        try { result = await ocr.ReadAsync(file, ct); }
        catch (OcrProviderUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new OcrResult(
                $"OCR çalıştırılamadı: {ex.Message} Tutarları elle girin.", null, null, null, null, null, null, storedName
            ));
        }
        catch (OcrDocumentProcessingException ex)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Belge OCR ile okunamadı",
                Detail = "Dosya saklandı. Rapor tutarlarını elle girebilirsiniz.",
            };
            problem.Extensions["documentPath"] = storedName;
            problem.Extensions["ocrRawText"] = ex.Message;
            return UnprocessableEntity(problem);
        }
        return Ok(result with { DocumentPath = storedName });
    }

    private static async Task<bool> HasValidSignatureAsync(IFormFile file, string extension, CancellationToken ct)
    {
        var signature = new byte[8];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(signature.AsMemory(), ct);
        return extension switch
        {
            ".png" => read >= 8 && signature.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => read >= 3 && signature[0] == 255 && signature[1] == 216 && signature[2] == 255,
            ".pdf" => read >= 5 && signature.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            _ => false,
        };
    }

    static ZReport Map(ZReportRequest r) =>
        new()
        {
            ReportDate = r.ReportDate,
            GrossSales = r.GrossSales,
            TotalVat = r.TotalVat,
            NetSales = r.NetSales,
            CashAmount = r.CashAmount,
            CardAmount = r.CardAmount,
            TotalAmount = r.TotalAmount,
            DocumentPath = r.DocumentPath,
            OcrRawText = r.OcrRawText,
            IsConfirmed = r.IsConfirmed,
        };
}

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DashboardController(ApplicationDbContext db, ManagementService svc) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var a = await svc.AccountingSummary();
        var c = await svc.CashSummary();
        return Ok(
            new DashboardSummary(
                await db.Customers.CountAsync(x => x.IsActive),
                await db.Employees.CountAsync(x => x.IsActive),
                a.TotalIncome,
                a.TotalExpense,
                a.NetBalance,
                c.TotalCashIn,
                c.TotalCashOut,
                c.CurrentBalance,
                await db
                    .AccountingTransactions.OrderByDescending(x => x.TransactionDate)
                    .Take(5)
                    .ToListAsync(),
                await db
                    .CashTransactions.OrderByDescending(x => x.TransactionDate)
                    .Take(5)
                    .ToListAsync(),
                await db.ZReports.OrderByDescending(x => x.ReportDate).Take(5).ToListAsync()
            )
        );
    }
}
