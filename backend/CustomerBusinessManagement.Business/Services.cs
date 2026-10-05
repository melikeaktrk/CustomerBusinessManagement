using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.DTO;
using CustomerBusinessManagement.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CustomerBusinessManagement.Business;

public interface IZReportOcrService
{
    Task<OcrResult> ReadAsync(IFormFile file, CancellationToken ct);
}

public sealed class OcrProviderUnavailableException(string message) : Exception(message);
public sealed class OcrDocumentProcessingException(string message) : Exception(message);

/// <summary>Belgeyi gerçek Tesseract OCR motoruyla okur; tanınmayan alanları boş bırakır.</summary>
public sealed class ZReportOcrService(IConfiguration configuration) : IZReportOcrService
{
    public async Task<OcrResult> ReadAsync(IFormFile file, CancellationToken ct)
    {
        // Boş değer bırakıldığında Windows PATH kullanılır; ortam değişkenleri JSON ayarlarını ezer.
        var tesseract = ResolveExecutable(configuration["Ocr:TesseractPath"], "tesseract");
        var languages = string.IsNullOrWhiteSpace(configuration["Ocr:Languages"])
            ? "tur+eng"
            : configuration["Ocr:Languages"]!;
        var tessdataPath = configuration["Ocr:TessdataPath"];
        var fileExtension = Path.GetExtension(file.FileName);
        string rawText;

        if (fileExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            rawText = await ReadPdfAsync(file, tesseract, languages, tessdataPath, ct);
        }
        else
        {
            await using var input = file.OpenReadStream();
            rawText = await RunProcessAsync(
                tesseract,
                BuildTesseractArguments(languages, tessdataPath),
                input,
                ct
            );
        }

        return ZReportOcrParser.Parse(rawText);
    }

    private async Task<string> ReadPdfAsync(
        IFormFile file,
        string tesseract,
        string languages,
        string? tessdataPath,
        CancellationToken ct
    )
    {
        var pdfToPpm = ResolveExecutable(configuration["Ocr:PdfToPpmPath"], "pdftoppm");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"zreport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var pdfPath = Path.Combine(temporaryDirectory, "report.pdf");
            await using (var output = File.Create(pdfPath))
                await file.CopyToAsync(output, ct);

            var outputPrefix = Path.Combine(temporaryDirectory, "page");
            await RunProcessAsync(
                pdfToPpm,
                ["-png", "-r", "250", pdfPath, outputPrefix],
                null,
                ct
            );
            var pages = Directory.GetFiles(temporaryDirectory, "page-*.png")
                .OrderBy(x => x, StringComparer.Ordinal);
            var text = new StringBuilder();
            foreach (var page in pages)
            {
                await using var image = File.OpenRead(page);
                text.AppendLine(await RunProcessAsync(
                    tesseract,
                    BuildTesseractArguments(languages, tessdataPath),
                    image,
                    ct
                ));
            }
            if (text.Length == 0)
                throw new OcrDocumentProcessingException(
                    "PDF dosyasından okunabilir sayfa görüntüsü üretilemedi. Belgenin bozuk olmadığını kontrol edin."
                );
            return text.ToString();
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); }
            catch (IOException) { /* Geçici dosyaların silinmesi sonraki sistem temizliğine bırakılır. */ }
        }
    }

    private static async Task<string> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        Stream? standardInput,
        CancellationToken ct
    )
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new OcrProviderUnavailableException("OCR işlemi başlatılamadı.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new OcrProviderUnavailableException(
                $"OCR bağımlılığı bulunamadı veya çalıştırılamadı ({Path.GetFileName(executable)}). TesseractPath ve PDF kullanılıyorsa PdfToPpmPath ayarlarını kontrol edin."
            );
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        if (standardInput is not null)
        {
            await standardInput.CopyToAsync(process.StandardInput.BaseStream, ct);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(ct);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            if (error.Contains("Error opening data file", StringComparison.OrdinalIgnoreCase)
                || error.Contains("Failed loading language", StringComparison.OrdinalIgnoreCase))
                throw new OcrProviderUnavailableException(
                    "Tesseract çalışıyor ancak seçilen OCR dil dosyaları bulunamadı. Ocr:TessdataPath ile tur.traineddata ve eng.traineddata dosyalarının bulunduğu klasörü gösterin."
                );

            throw new OcrDocumentProcessingException(
                "Dosya doğrulandı ancak OCR motoru içeriği okuyamadı. Dosyanın bozuk olmadığını ve desteklenen bir PNG, JPG veya PDF olduğunu kontrol edin."
            );
        }
        return output;
    }

    private static string ResolveExecutable(string? configuredPath, string pathCommand) =>
        string.IsNullOrWhiteSpace(configuredPath) ? pathCommand : configuredPath.Trim();

    private static List<string> BuildTesseractArguments(string languages, string? tessdataPath)
    {
        var arguments = new List<string> { "stdin", "stdout" };
        if (!string.IsNullOrWhiteSpace(tessdataPath))
        {
            arguments.Add("--tessdata-dir");
            arguments.Add(tessdataPath.Trim());
        }
        arguments.AddRange(["-l", languages, "--psm", "6"]);
        return arguments;
    }

}

/// <summary>Gerçek OCR metnindeki etiketli tutarları Türkçe para yazımlarını gözeterek ayıklar.</summary>
public static class ZReportOcrParser
{
    public static OcrResult Parse(string rawText)
    {
        // Satır sonları etiket ile tutar arasında gelebilir; ham OCR metni yanıtta korunur.
        var normalizedText = NormalizeForMatching(
            Regex.Replace(rawText, @"(?<=\p{L})[ \t]*\r?\n[ \t]*(?=\p{L})", " ")
        );
        var gross = ParseAmount(
            normalizedText,
            "Toplam Ciro",
            "Toplam Cirosu",
            "Brüt satış",
            "Brut satis",
            "Gross Sales",
            "GrossSales"
        );
        var vat = ParseAmount(
            normalizedText,
            "KDV Tutarı",
            "KDV Tutari",
            "Toplam KDV",
            "Total VAT",
            "TotalVat",
            "KDV"
        );
        var net = ParseAmount(normalizedText, "Net satış", "Net satis", "Net Sales", "NetSales");
        var cash = ParseAmount(normalizedText, "Nakit", "Cash", "CashAmount");
        var card = ParseAmount(normalizedText, "Kart", "Kredi kartı", "Card", "CardAmount");
        var total = ParseAmount(normalizedText, "Genel toplam", "Toplam tutar", "Total", "TotalAmount");
        net ??= gross.HasValue && vat.HasValue ? gross - vat : null;
        total ??= cash.HasValue && card.HasValue ? cash + card : null;

        return new OcrResult(rawText.Trim(), gross, vat, net, cash, card, total);
    }

    private static decimal? ParseAmount(string text, params string[] labels)
    {
        foreach (var label in labels)
        {
            var flexibleLabel = Regex.Replace(
                Regex.Escape(NormalizeForMatching(label)),
                @"\\ ",
                @"\s+"
            );
            var match = Regex.Match(
                text,
                $@"{flexibleLabel}\s*[:\-]?\s*(?:₺\s*|TL\s*)?([\d.,]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );
            if (!match.Success) continue;

            var value = match.Groups[1].Value.Trim();
            var decimalSeparator = value.LastIndexOfAny([',', '.']);
            var normalized = decimalSeparator >= 0 && value.Length - decimalSeparator - 1 is 1 or 2
                ? Regex.Replace(value[..decimalSeparator], "[.,]", "") + "." + value[(decimalSeparator + 1)..]
                : Regex.Replace(value, "[.,]", "");
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                return amount;
        }
        return null;
    }

    private static string NormalizeForMatching(string value) =>
        value.Replace('İ', 'i').Replace('ı', 'i').Replace('I', 'i').ToLowerInvariant();
}

public static class FinancialTotals
{
    public static AccountingSummary Accounting(IEnumerable<AccountingTransaction> rows)
    {
        var active = rows.Where(x => !x.IsCancelled);
        var income = active.Where(x => x.TransactionType == AccountingType.Income).Sum(x => x.Amount);
        var expense = active.Where(x => x.TransactionType == AccountingType.Expense).Sum(x => x.Amount);
        return new(income, expense, income - expense);
    }

    public static CashSummary Cash(IEnumerable<CashTransaction> rows)
    {
        var active = rows.Where(x => !x.IsCancelled);
        var ins = active.Where(x => x.TransactionType == CashType.CashIn).Sum(x => x.Amount);
        var outs = active.Where(x => x.TransactionType == CashType.CashOut).Sum(x => x.Amount);
        return new(ins, outs, ins - outs);
    }
}

public sealed class ManagementService(ApplicationDbContext db)
{
    public async Task<AccountingSummary> AccountingSummary() =>
        FinancialTotals.Accounting(await db.AccountingTransactions.ToListAsync());

    public async Task<CashSummary> CashSummary() =>
        FinancialTotals.Cash(await db.CashTransactions.ToListAsync());
}
