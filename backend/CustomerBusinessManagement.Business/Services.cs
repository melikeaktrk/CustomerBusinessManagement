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
        var ocrSpaceKey = configuration["Ocr:OcrSpaceApiKey"];
        if (!string.IsNullOrWhiteSpace(ocrSpaceKey))
        {
            var configuredEngine = configuration["Ocr:OcrSpaceEngine"] ?? "2";
            var isPdf = Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            if (isPdf) configuredEngine = "1";

            var ocrSpacePassTexts = new List<string>
            {
                await ReadWithOcrSpaceAsync(file, ocrSpaceKey.Trim(), configuredEngine, ct)
            };
            var alternateEngine = configuredEngine == "1" ? "2" : "1";
            try
            {
                ocrSpacePassTexts.Add(await ReadWithOcrSpaceAsync(file, ocrSpaceKey.Trim(), alternateEngine, ct));
            }
            catch (OcrProviderUnavailableException) when (!ct.IsCancellationRequested)
            {
                // Preserve the successful configured-engine pass if the alternate is unavailable.
            }
            catch (OcrDocumentProcessingException) when (!ct.IsCancellationRequested)
            {
                // An alternate engine can reject a format the configured engine accepted.
            }
            return ZReportOcrParser.ParseBest(ocrSpacePassTexts);
        }

        // Boş değer bırakıldığında Windows PATH kullanılır; ortam değişkenleri JSON ayarlarını ezer.
        var tesseract = ResolveExecutable(configuration["Ocr:TesseractPath"], "tesseract.exe");
        var languages = string.IsNullOrWhiteSpace(configuration["Ocr:Languages"])
            ? "tur+eng"
            : configuration["Ocr:Languages"]!;
        var tessdataPath = ResolveConfiguredPath(configuration["Ocr:TessdataPath"]);
        var fileExtension = Path.GetExtension(file.FileName);
        if (fileExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pageTexts = await ReadPdfAsync(file, tesseract, languages, tessdataPath, ct);
            return ZReportOcrParser.ParseBest(pageTexts);
        }

        // Thermal receipts are narrow and often photographed at an angle. Run several
        // Tesseract page-segmentation modes so labels missed in one pass can be recovered.
        var passTexts = new List<string>();
        foreach (var pageSegmentationMode in new[] { "4", "6", "11" })
        {
            await using var input = file.OpenReadStream();
            passTexts.Add(await RunProcessAsync(
                tesseract,
                BuildTesseractArguments(languages, tessdataPath, pageSegmentationMode),
                input,
                ct
            ));
        }
        return ZReportOcrParser.ParseBest(passTexts);
    }

    private static readonly HttpClient OcrSpaceClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    private async Task<string> ReadWithOcrSpaceAsync(
        IFormFile file,
        string apiKey,
        string engine,
        CancellationToken ct
    )
    {
        var isPdf = Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(apiKey), "apikey");
        form.Add(new StringContent("tur"), "language");
        form.Add(new StringContent("true"), "isTable");
        form.Add(new StringContent("true"), "scale");
        form.Add(new StringContent(engine), "OCREngine");

        await using var stream = file.OpenReadStream();
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        form.Add(fileContent, "file", file.FileName);

        HttpResponseMessage response;
        try
        {
            response = await OcrSpaceClient.PostAsync("https://api.ocr.space/parse/image", form, ct);
        }
        catch (HttpRequestException)
        {
            throw new OcrProviderUnavailableException("OCR servisine ulaşılamadı.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OcrProviderUnavailableException("OCR servisi zaman aşımına uğradı.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new OcrProviderUnavailableException("OCR servisi geçici olarak kullanılamıyor.");

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("IsErroredOnProcessing", out var errored)
                && errored.ValueKind == System.Text.Json.JsonValueKind.True)
                throw new OcrProviderUnavailableException(
                    "OCR servisi dosyayı işleyemedi (dosya çok büyük olabilir).");

            if (!root.TryGetProperty("ParsedResults", out var results)
                || results.ValueKind != System.Text.Json.JsonValueKind.Array
                || results.GetArrayLength() == 0)
                throw new OcrDocumentProcessingException(
                    "Dosya doğrulandı ancak OCR motoru içeriği okuyamadı. Dosyanın bozuk olmadığını ve desteklenen bir PNG, JPG veya PDF olduğunu kontrol edin.");

            var texts = new List<string>();
            foreach (var r in results.EnumerateArray())
                if (r.TryGetProperty("ParsedText", out var t) && t.GetString() is { } s)
                    texts.Add(s);
            return string.Join("\n", texts);
        }
    }

    private async Task<List<string>> ReadPdfAsync(
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
            var texts = new List<string>();
            foreach (var page in pages)
            {
                foreach (var pageSegmentationMode in new[] { "4", "6", "11" })
                {
                    await using var image = File.OpenRead(page);
                    texts.Add(await RunProcessAsync(
                        tesseract,
                        BuildTesseractArguments(languages, tessdataPath, pageSegmentationMode),
                        image,
                        ct
                    ));
                }
            }
            if (texts.Count == 0)
                throw new OcrDocumentProcessingException(
                    "PDF dosyasından okunabilir sayfa görüntüsü üretilemedi. Belgenin bozuk olmadığını kontrol edin."
                );
            return texts;
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
                "OCR bağımlılığı bulunamadı veya çalıştırılamadı."
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
        string.IsNullOrWhiteSpace(configuredPath)
            ? pathCommand
            : ResolveConfiguredPath(configuredPath)!;

    private static string? ResolveConfiguredPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        var path = configuredPath.Trim();
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, AppContext.BaseDirectory);
    }

    private static List<string> BuildTesseractArguments(
        string languages,
        string? tessdataPath,
        string pageSegmentationMode = "4"
    )
    {
        var arguments = new List<string> { "stdin", "stdout" };
        if (!string.IsNullOrWhiteSpace(tessdataPath))
        {
            arguments.Add("--tessdata-dir");
            arguments.Add(tessdataPath.Trim());
        }
        arguments.AddRange(["-l", languages, "--psm", pageSegmentationMode]);
        return arguments;
    }

}

/// <summary>Gerçek OCR metnindeki etiketli tutarları Türkçe para yazımlarını gözeterek ayıklar.</summary>
public static class ZReportOcrParser
{
    public static OcrResult ParseBest(IEnumerable<string> rawTexts)
    {
        var texts = rawTexts.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
        if (texts.Count == 0) return Parse(string.Empty);

        var parsed = texts.Select(Parse).ToList();
        T? Pick<T>(Func<OcrResult, T?> selector) where T : struct => parsed
            .Select((result, index) => new { Value = selector(result), Score = CompletenessScore(result), Index = index })
            .Where(candidate => candidate.Value.HasValue)
            .GroupBy(candidate => candidate.Value!.Value)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(candidate => candidate.Score))
            .ThenBy(group => group.Min(candidate => candidate.Index))
            .Select(group => (T?)group.Key)
            .FirstOrDefault();

        return new OcrResult(
            string.Join("\r\n--- OCR PASS ---\r\n", texts),
            Pick(result => result.GrossSales),
            Pick(result => result.TotalVat),
            Pick(result => result.NetSales),
            Pick(result => result.CashAmount),
            Pick(result => result.CardAmount),
            Pick(result => result.TotalAmount),
            null,
            Pick(result => result.OksFisleriCount),
            Pick(result => result.KdvToplami),
            Pick(result => result.SatisToplami),
            Pick(result => result.Diger),
            Pick(result => result.IptalCount),
            Pick(result => result.IptalKdvToplami),
            Pick(result => result.IptalSatisToplami),
            Pick(result => result.Nakit),
            Pick(result => result.Kredi)
        );
    }

    private static int CompletenessScore(OcrResult result) =>
        new object?[]
        {
            result.GrossSales, result.TotalVat, result.NetSales, result.CashAmount,
            result.CardAmount, result.TotalAmount, result.OksFisleriCount,
            result.KdvToplami, result.SatisToplami, result.Nakit, result.Kredi,
            result.Diger, result.IptalCount, result.IptalKdvToplami, result.IptalSatisToplami
        }.Count(value => value is not null);

    public static OcrResult Parse(string rawText)
    {
        // Satır sonları etiket ile tutar arasında gelebilir; ham OCR metni yanıtta korunur.
        var normalizedText = NormalizeForMatching(rawText);
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
            "TotalVat"
        );
        var net = ParseAmount(normalizedText, "Net satış", "Net satis", "Net Sales", "NetSales");
        var payments = ExtractSection(normalizedText, "odeme bilgileri", "belge t");
        var cash = ParsePaymentTotal(payments, "nakit");
        var credit = ParsePaymentTotal(payments, "kredi");
        var qrCard = ParsePaymentTotal(payments, "karekod kart");
        var card = credit.HasValue && qrCard.HasValue ? credit + qrCard : credit ?? qrCard;
        var total = cash.HasValue && card.HasValue ? cash + card : null;
        var documentTypes = ExtractSection(normalizedText, "belge tipleri", "sayaclar");
        if (string.IsNullOrWhiteSpace(documentTypes))
            documentTypes = ExtractSection(normalizedText, "belge t", "sayaclar");
        // OCR may omit the section title, split the heading, or return only the receipt lines.
        // Locate the receipt-count label and use it as the section start in those cases.
        if (string.IsNullOrWhiteSpace(documentTypes))
        {
            var receiptAnchor = Regex.Match(
                normalizedText,
                @"(?:o.?k.?c[\s\S]{0,12})?fisler[i1l]?",
                RegexOptions.CultureInvariant
            );
            if (receiptAnchor.Success)
            {
                var candidate = normalizedText[receiptAnchor.Index..];
                if (candidate.Contains("satis top", StringComparison.Ordinal))
                {
                    var countersStart = candidate.IndexOf("sayac", StringComparison.Ordinal);
                    documentTypes = countersStart < 0 ? candidate : candidate[..countersStart];
                }
            }
        }
        // Last resort for OCR output that lost the receipt-count label but retained the totals.
        if (string.IsNullOrWhiteSpace(documentTypes)
            && normalizedText.Contains("satis top", StringComparison.Ordinal)
            && normalizedText.Contains("iptal", StringComparison.Ordinal))
            documentTypes = normalizedText;
        var voidSection = ExtractSection(documentTypes, "iptal", null);
        if (string.IsNullOrWhiteSpace(voidSection))
            voidSection = ExtractSection(documentTypes, "ipta", null);
        var receiptCount = ParseIntegerOnLine(documentTypes, "okc fisleri");
        var documentVat = ParseAmountOnLine(documentTypes, "kdv toplami", "kdv. toplami", "kov toplami", "kov. toplami");
        // Z reports commonly repeat the regular document VAT in KDV BİLGİLERİ.
        // On that row, OCR can see the rate (10.00) before the actual VAT amount;
        // take the rightmost amount on the tax row as a stronger cross-check.
        var vatDetails = ExtractSection(normalizedText, "kdv bilgileri", "departman bilgileri");
        var vatFromDetails = ParseRightmostAmountOnLine(vatDetails, "kdv", "kov", "kon");
        var dailySummary = ExtractSection(normalizedText, "gunluk fis dokumu", "departman bilgileri");
        var vatFromDailySummary = ParseAmountOnLine(dailySummary, "topkdv", "top kdv", "topkov", "top kov");
        if (vatFromDailySummary.HasValue)
            documentVat = vatFromDailySummary;
        if (vatFromDetails.HasValue)
            documentVat = vatFromDetails;
        var documentSales = ParseAmountOnLine(documentTypes, "satis toplami", "satis toplani", "satis topl");
        var documentCash = ParseAmountOnLine(documentTypes, "nakit");
        var documentCard = ParseAmountOnLine(documentTypes, "kredi");
        var other = ParseAmountOnLine(documentTypes, "diger");
        // OCR providers sometimes split or truncate the detected section. Recover any
        // missing document values from their labels in the complete OCR text.
        receiptCount ??= ParseIntegerOnLine(normalizedText, "okc fisleri");
        documentVat ??= ParseAmountOnLine(normalizedText, "kdv toplami", "kdv. toplami", "kov toplami", "kov. toplami");
        documentSales ??= ParseAmountOnLine(normalizedText, "satis toplami", "satis toplani", "satis topl");
        documentCash ??= ParseAmountOnLine(normalizedText, "nakit");
        documentCard ??= ParseAmountOnLine(normalizedText, "kredi");
        other ??= ParseAmountOnLine(normalizedText, "diger");
        if (!other.HasValue && documentSales.HasValue && documentCash.HasValue && documentCard.HasValue)
        {
            var inferredOther = documentSales.Value - documentCash.Value - documentCard.Value;
            if (inferredOther >= 0) other = inferredOther;
        }
        if (string.IsNullOrWhiteSpace(voidSection))
        {
            voidSection = ExtractSection(normalizedText, "iptal", null);
            if (string.IsNullOrWhiteSpace(voidSection))
                voidSection = ExtractSection(normalizedText, "ipta", null);
        }
        // OCR frequently drops the final L in İPTAL (for example, "İPTA 10").
        var cancelledCount = ParseIntegerOnLine(voidSection, "iptal", "ipta");
        var cancelledVat = ParseAmountOnLine(voidSection, "kdv toplami", "kdv. toplami", "kov toplami", "kov. toplami");
        var cancelledSales = ParseAmountOnLine(voidSection, "satis toplami", "satis toplani", "satis topl");
        cancelledCount ??= ParseIntegerOnLine(normalizedText, "satis iptal", "iptal satis");
        cancelledVat ??= ParseAmountOnLineOccurrence(documentTypes, 1, "kdv toplami", "kdv. toplami", "kov toplami", "kov. toplami");
        cancelledSales ??= ParseAmountOnLineOccurrence(documentTypes, 1, "satis toplami", "satis toplani", "satis topl");

        // Sales in BELGE TİPLERİ must reconcile with its cash, credit, and other totals.
        // OCR can attach a stray leading digit to the sales figure (21.890 -> 421.890).
        if (documentCash.HasValue && documentCard.HasValue && other.HasValue)
        {
            var paymentBreakdownTotal = documentCash.Value + documentCard.Value + other.Value;
            if (paymentBreakdownTotal > 0 && documentSales != paymentBreakdownTotal)
                documentSales = paymentBreakdownTotal;
        }

        // The daily receipt summary is a clearer duplicate of document sales on some slips.
        // OCR may prefix a spurious digit to the BELGE TİPLERİ amount (21.890 -> 421.890).
        var dailySales = ParseAmount(ExtractSection(normalizedText, "gunluk fis dokumu", "kdv bilgileri"), "toplam");
        if (dailySales.HasValue && documentSales > dailySales * 2)
            documentSales = dailySales;
        // BELGE TİPLERİ KDV'si OCR'da bozulabiliyor (ör. "5:" -> 5,00). Rapordaki tüm KDV
        // satırlarından makul adayları toplayıp satış tutarıyla doğrula.
        documentVat = ResolveDocumentVat(normalizedText, documentVat, documentSales);
        gross ??= documentSales;
        // Genel "KDV" etiketi, "KDV %10.00" satırındaki oranı (410.00) tutar sanabilir; belge KDV'si önceliklidir.
        vat ??= documentVat;
        vat ??= ParseAmount(normalizedText, "KDV");
        cash = documentCash ?? cash ?? ParseAmount(normalizedText, "Nakit", "Cash", "CashAmount");
        card = documentCard ?? card ?? ParseAmount(normalizedText, "Kart", "Kredi kartı", "Kredi", "Card", "CardAmount");
        net ??= gross.HasValue && vat.HasValue ? gross - vat : null;
        total = documentSales ?? total ?? (cash.HasValue && card.HasValue ? cash + card : null);
        return new OcrResult(
            rawText.Trim(), gross, vat, net, cash, card, total,
            null, receiptCount, documentVat, documentSales, other,
            cancelledCount, cancelledVat, cancelledSales, documentCash, documentCard
        );
    }


    private static readonly decimal[] CommonVatRates = [1m, 8m, 10m, 18m, 20m];

    /// <summary>
    /// Z raporunda belge KDV toplamı birden fazla yerde (TOPKDV, KDV BİLGİLERİ, BELGE TİPLERİ) tekrarlanır.
    /// Tüm KDV satırlarındaki kesin para formatlı tutarları toplar, satış tutarına göre makul olmayanları
    /// (oran, "5:" gibi artıklar, toplam satış vb.) eler ve en çok tekrar eden/oranla uyumlu olanı seçer.
    /// </summary>
    private static decimal? ResolveDocumentVat(string normalizedText, decimal? current, decimal? sales)
    {
        if (!sales.HasValue || sales.Value <= 0) return current;
        var salesValue = sales.Value;
        bool Plausible(decimal value) => value >= salesValue * 0.0099m && value <= salesValue * 0.1667m;
        decimal RateDistance(decimal value) => CommonVatRates
            .Select(rate => Math.Abs(value - salesValue * rate / (100m + rate)))
            .Min();

        var amountPattern = new Regex(@"\d{1,3}(?:[.,\s]?\d{3})*[.,]\s?\d{2}(?!\d)", RegexOptions.CultureInvariant);
        var lines = normalizedText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var counts = new Dictionary<decimal, int>();

        void Collect(string line)
        {
            foreach (Match match in amountPattern.Matches(line))
            {
                var amount = ParseReceiptAmount(match.Value);
                if (amount.HasValue && Plausible(amount.Value))
                    counts[amount.Value] = counts.GetValueOrDefault(amount.Value) + 1;
            }
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!Regex.IsMatch(line, @"k[dov][vn]", RegexOptions.CultureInvariant)) continue;
            var before = counts.Values.Sum();
            Collect(line);
            // Tutar bir sonraki satıra kaymış olabilir; başlık satırlarına bakma.
            if (counts.Values.Sum() == before && !line.Contains("bilgi", StringComparison.Ordinal) && index + 1 < lines.Length)
                Collect(lines[index + 1]);
        }

        if (counts.Count == 0) return current.HasValue && Plausible(current.Value) ? current : null;

        var best = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => RateDistance(pair.Key))
            .First();
        if (current.HasValue && Plausible(current.Value) && counts.GetValueOrDefault(current.Value) >= best.Value)
            return current;
        return best.Key;
    }

    private static string ExtractSection(string text, string startLabel, string? endLabel)
    {
        var start = text.IndexOf(startLabel, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        var section = text[(start + startLabel.Length)..];
        var end = endLabel is null ? -1 : section.IndexOf(endLabel, StringComparison.OrdinalIgnoreCase);
        var sectionStart = start + startLabel.Length;
        return end < 0 ? text[start..] : text[start..(sectionStart + end)];
    }

    private static int? ParseInteger(string text, params string[] labels)
    {
        var amount = ParseAmount(text, labels);
        return amount.HasValue ? decimal.ToInt32(decimal.Truncate(amount.Value)) : null;
    }

    private static decimal? ParsePaymentTotal(string text, string itemLabel)
    {
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            if (!NormalizeForMatching(lines[index]).Contains(itemLabel, StringComparison.Ordinal))
                continue;
            for (var totalIndex = index + 1; totalIndex < Math.Min(lines.Length, index + 3); totalIndex++)
            {
                if (!NormalizeForMatching(lines[totalIndex]).Contains("toplam", StringComparison.Ordinal)
                    && !NormalizeForMatching(lines[totalIndex]).Contains("toplan", StringComparison.Ordinal))
                    continue;
                return ParseAmountOnLine(lines[totalIndex], "toplam", "toplan");
            }
        }
        return null;
    }

    private static int? ParseIntegerOnLine(string text, params string[] labels)
    {
        var amount = ParseAmountOnLine(text, labels);
        return amount.HasValue ? decimal.ToInt32(decimal.Truncate(amount.Value)) : null;
    }

    private static decimal? ParseAmountOnLine(string text, params string[] labels)
        => ParseAmountOnLineOccurrence(text, 0, labels);

    private static decimal? ParseRightmostAmountOnLine(string text, params string[] labels)
    {
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedLine = NormalizeForMatching(line);
            var label = labels
                .Where(candidate => normalizedLine.Contains(candidate, StringComparison.Ordinal))
                .OrderByDescending(candidate => candidate.Length)
                .FirstOrDefault();
            if (label is null) continue;

            var labelIndex = normalizedLine.IndexOf(label, StringComparison.Ordinal) + label.Length;
            var amounts = Regex.Matches(normalizedLine[labelIndex..], @"[0-9b][0-9b., ]*")
                .Select(match => ParseReceiptAmount(match.Value))
                .Where(amount => amount.HasValue)
                .Select(amount => amount!.Value)
                .ToList();
            if (amounts.Count > 0) return amounts[^1];
        }
        return null;
    }

    private static decimal? ParseReceiptAmount(string rawValue)
    {
        var value = rawValue.Trim().Replace('b', '8').Replace(" ", "");
        var decimalSeparator = value.LastIndexOfAny([',', '.']);
        var normalized = decimalSeparator >= 0 && value.Length - decimalSeparator - 1 is 1 or 2
            ? Regex.Replace(value[..decimalSeparator], "[.,]", "") + "." + value[(decimalSeparator + 1)..]
            : Regex.Replace(value, "[.,]", "");
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }

    private static decimal? ParseAmountOnLineOccurrence(string text, int occurrence, params string[] labels)
    {
        var parsedCount = 0;
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var normalizedLine = NormalizeForMatching(lines[lineIndex]);
            var matches = new List<(int Index, int Length)>();
            foreach (var label in labels)
            {
                var index = normalizedLine.IndexOf(label, StringComparison.Ordinal);
                while (index >= 0)
                {
                    matches.Add((index, label.Length));
                    index = normalizedLine.IndexOf(label, index + label.Length, StringComparison.Ordinal);
                }
            }

            foreach (var match in matches
                .GroupBy(x => x.Index)
                .Select(group => group.OrderByDescending(x => x.Length).First())
                .OrderBy(x => x.Index))
            {
                var candidateLines = new[] { normalizedLine[(match.Index + match.Length)..] }
                    .Concat(lineIndex + 1 < lines.Length ? [NormalizeForMatching(lines[lineIndex + 1])] : []);
                foreach (var candidate in candidateLines)
                {
                    var valueMatch = Regex.Match(candidate, @"\*?[^0-9b\r\n]*([0-9b][0-9., ]*)");
                    if (!valueMatch.Success) continue;
                    var value = valueMatch.Groups[1].Value.Trim().Replace('b', '8').Replace(" ", "");
                    var decimalSeparator = value.LastIndexOfAny([',', '.']);
                    var normalized = decimalSeparator >= 0 && value.Length - decimalSeparator - 1 is 1 or 2
                        ? Regex.Replace(value[..decimalSeparator], "[.,]", "") + "." + value[(decimalSeparator + 1)..]
                        : Regex.Replace(value, "[.,]", "");
                    if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                    {
                        if (parsedCount++ == occurrence) return amount;
                        break;
                    }
                }
            }
        }
        return null;
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
                $@"{flexibleLabel}\s*[:\-]?\s*\*?\s*(?:₺\s*|TL\s*)?([\d][\d.,\s]*[\d]|[\d])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );
            if (!match.Success) continue;

            var value = Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", "");
            var decimalSeparator = value.LastIndexOfAny([',', '.']);
            var normalized = decimalSeparator >= 0 && value.Length - decimalSeparator - 1 is 1 or 2
                ? Regex.Replace(value[..decimalSeparator], "[.,]", "") + "." + value[(decimalSeparator + 1)..]
                : Regex.Replace(value, "[.,]", "");
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                return amount;
        }
        return null;
    }

    private static string NormalizeForMatching(string value)
    {
        var decomposed = value
            .Replace('İ', 'i')
            .Replace('ı', 'i')
            .Replace('I', 'i')
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
        )).Normalize(NormalizationForm.FormC);
    }
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