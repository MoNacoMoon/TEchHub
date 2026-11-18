using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace EduCabinet.Web.Services;

public class DocumentContentExtractorService : IDocumentContentExtractorService
{
    private readonly ILogger<DocumentContentExtractorService> _logger;

    public DocumentContentExtractorService(ILogger<DocumentContentExtractorService> logger)
    {
        _logger = logger;
    }

    public string? ExtractContentHtml(string fullDiskPath, string? originalFileName)
    {
        if (string.IsNullOrWhiteSpace(fullDiskPath) || !File.Exists(fullDiskPath))
        {
            return null;
        }

        var ext = Path.GetExtension(originalFileName ?? fullDiskPath).ToLowerInvariant();
        if (ext != ".docx" && ext != ".pptx" && ext != ".txt" && ext != ".md")
        {
            return null;
        }

        var cachePath = fullDiskPath + ".extracted.html";
        try
        {
            if (File.Exists(cachePath))
            {
                var originalModified = File.GetLastWriteTimeUtc(fullDiskPath);
                var cacheModified = File.GetLastWriteTimeUtc(cachePath);
                if (cacheModified >= originalModified)
                {
                    var cached = File.ReadAllText(cachePath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(cached))
                    {
                        return cached;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать кэш извлеченного контента: {CachePath}", cachePath);
        }

        string? html = null;
        try
        {
            html = ext switch
            {
                ".docx" => ExtractDocx(fullDiskPath),
                ".pptx" => ExtractPptx(fullDiskPath),
                ".txt" or ".md" => ExtractText(fullDiskPath),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(html))
            {
                try
                {
                    File.WriteAllText(cachePath, html, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось сохранить кэш извлеченного контента: {CachePath}", cachePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при извлечении текстового контента из документа: {Path}", fullDiskPath);
            return null;
        }

        return html;
    }

    private string? ExtractDocx(string fullDiskPath)
    {
        using var stream = File.OpenRead(fullDiskPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var docEntry = archive.GetEntry("word/document.xml");
        if (docEntry == null) return null;

        using var entryStream = docEntry.Open();
        var xdoc = XDocument.Load(entryStream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var body = xdoc.Root?.Element(w + "body");
        if (body == null) return null;

        var sb = new StringBuilder();
        int contentBlocksCount = 0;

        foreach (var element in body.Elements())
        {
            if (element.Name == w + "p")
            {
                var paragraphHtml = ParseDocxParagraph(element, w);
                if (!string.IsNullOrWhiteSpace(paragraphHtml))
                {
                    sb.AppendLine(paragraphHtml);
                    contentBlocksCount++;
                }
            }
            else if (element.Name == w + "tbl")
            {
                var tableHtml = ParseDocxTable(element, w);
                if (!string.IsNullOrWhiteSpace(tableHtml))
                {
                    sb.AppendLine(tableHtml);
                    contentBlocksCount++;
                }
            }
        }

        if (contentBlocksCount == 0) return null;

        return $@"<div class=""extracted-docx-content docx-typography"">
{sb}
</div>";
    }

    private string? ParseDocxParagraph(XElement p, XNamespace w)
    {
        var pPr = p.Element(w + "pPr");
        var styleVal = pPr?.Element(w + "pStyle")?.Attribute(w + "val")?.Value?.ToLowerInvariant();
        bool isList = pPr?.Element(w + "numPr") != null;

        var textSb = new StringBuilder();
        foreach (var child in p.Elements())
        {
            if (child.Name == w + "r")
            {
                var rPr = child.Element(w + "rPr");
                bool isBold = rPr?.Element(w + "b") != null;
                bool isItalic = rPr?.Element(w + "i") != null;
                bool isUnderline = rPr?.Element(w + "u") != null;
                bool isStrike = rPr?.Element(w + "strike") != null;

                var runTextSb = new StringBuilder();
                foreach (var runChild in child.Elements())
                {
                    if (runChild.Name == w + "t")
                    {
                        runTextSb.Append(WebUtility.HtmlEncode(runChild.Value));
                    }
                    else if (runChild.Name == w + "br")
                    {
                        runTextSb.Append("<br />");
                    }
                }

                if (runTextSb.Length > 0)
                {
                    string formatted = runTextSb.ToString();
                    if (isBold) formatted = $"<strong>{formatted}</strong>";
                    if (isItalic) formatted = $"<em>{formatted}</em>";
                    if (isUnderline) formatted = $"<u>{formatted}</u>";
                    if (isStrike) formatted = $"<s>{formatted}</s>";
                    textSb.Append(formatted);
                }
            }
            else if (child.Name == w + "br")
            {
                textSb.Append("<br />");
            }
        }

        var innerContent = textSb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(innerContent)) return null;

        if (!string.IsNullOrEmpty(styleVal))
        {
            if (styleVal.Contains("heading1") || styleVal.Contains("title") || styleVal.Contains("заголовок1"))
            {
                return $@"<h3 class=""fw-bold text-primary mt-4 mb-3"">{innerContent}</h3>";
            }
            if (styleVal.Contains("heading2") || styleVal.Contains("subtitle") || styleVal.Contains("заголовок2"))
            {
                return $@"<h4 class=""fw-bold text-secondary mt-3 mb-2"">{innerContent}</h4>";
            }
            if (styleVal.Contains("heading3") || styleVal.Contains("заголовок3"))
            {
                return $@"<h5 class=""fw-bold text-body mt-3 mb-2"">{innerContent}</h5>";
            }
        }

        if (isList)
        {
            return $@"<div class=""d-flex align-items-start gap-2 mb-2 ms-3"">
    <i class=""bi bi-check-circle-fill text-primary mt-1 flex-shrink-0"" style=""font-size: 0.85rem;""></i>
    <div class=""lh-base text-body"">{innerContent}</div>
</div>";
        }

        return $@"<p class=""lh-base text-body mb-3"">{innerContent}</p>";
    }

    private string? ParseDocxTable(XElement tbl, XNamespace w)
    {
        var rows = tbl.Elements(w + "tr").ToList();
        if (!rows.Any()) return null;

        var sb = new StringBuilder();
        sb.AppendLine(@"<div class=""table-responsive my-4"">");
        sb.AppendLine(@"<table class=""table table-bordered table-striped table-hover align-middle"">");
        sb.AppendLine(@"<tbody>");

        bool isFirstRow = true;
        foreach (var tr in rows)
        {
            sb.AppendLine("<tr>");
            var cells = tr.Elements(w + "tc").ToList();
            foreach (var tc in cells)
            {
                var cellTexts = new List<string>();
                foreach (var p in tc.Elements(w + "p"))
                {
                    var pText = ParseDocxParagraph(p, w);
                    if (!string.IsNullOrWhiteSpace(pText))
                    {
                        cellTexts.Add(pText);
                    }
                }
                var cellContent = cellTexts.Any() ? string.Join("", cellTexts) : "&nbsp;";
                if (isFirstRow)
                {
                    sb.AppendLine($@"<th class=""bg-light fw-bold"">{cellContent}</th>");
                }
                else
                {
                    sb.AppendLine($@"<td>{cellContent}</td>");
                }
            }
            sb.AppendLine("</tr>");
            isFirstRow = false;
        }

        sb.AppendLine(@"</tbody>");
        sb.AppendLine(@"</table>");
        sb.AppendLine(@"</div>");

        return sb.ToString();
    }

    private string? ExtractPptx(string fullDiskPath)
    {
        using var stream = File.OpenRead(fullDiskPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var slideEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && 
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e =>
            {
                var fileName = Path.GetFileNameWithoutExtension(e.FullName);
                var numStr = new string(fileName.Where(char.IsDigit).ToArray());
                return int.TryParse(numStr, out int n) ? n : 9999;
            })
            .ToList();

        if (!slideEntries.Any()) return null;

        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var slidesSb = new StringBuilder();
        int slideNumber = 1;

        foreach (var entry in slideEntries)
        {
            using var entryStream = entry.Open();
            var slideDoc = XDocument.Load(entryStream);

            var paragraphs = slideDoc.Descendants(a + "p").ToList();
            var lines = new List<string>();

            foreach (var p in paragraphs)
            {
                var lineSb = new StringBuilder();
                foreach (var r in p.Descendants(a + "t"))
                {
                    lineSb.Append(r.Value);
                }
                var text = lineSb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text);
                }
            }

            if (!lines.Any())
            {
                slideNumber++;
                continue;
            }

            var slideTitle = lines[0];
            var slidePoints = lines.Skip(1).ToList();

            slidesSb.AppendLine($@"
<div class=""card mb-4 border-0 shadow-sm slide-presentation-card"" style=""border-left: 4px solid #4f46e5 !important; border-radius: 12px; overflow: hidden; background: #ffffff;"">
    <div class=""card-header bg-light d-flex justify-content-between align-items-center py-2 px-3 border-bottom"">
        <span class=""badge bg-primary text-white fw-bold px-2 py-1""><i class=""bi bi-file-earmark-slides me-1""></i>Слайд {slideNumber} из {slideEntries.Count}</span>
        <small class=""text-muted fw-semibold text-truncate ms-2"" style=""max-width: 320px;"">{WebUtility.HtmlEncode(slideTitle)}</small>
    </div>
    <div class=""card-body p-4"">
        <h5 class=""fw-bold text-primary mb-3""><i class=""bi bi-easel2 me-2""></i>{WebUtility.HtmlEncode(slideTitle)}</h5>");

            if (slidePoints.Any())
            {
                slidesSb.AppendLine(@"<ul class=""list-unstyled mb-0 d-flex flex-column gap-2"">");
                foreach (var point in slidePoints)
                {
                    slidesSb.AppendLine($@"    <li class=""d-flex align-items-start gap-2"">
        <i class=""bi bi-caret-right-fill text-indigo mt-1 flex-shrink-0"" style=""color: #4f46e5;""></i>
        <span class=""lh-base text-body"">{WebUtility.HtmlEncode(point)}</span>
    </li>");
                }
                slidesSb.AppendLine(@"</ul>");
            }

            slidesSb.AppendLine(@"    </div>
</div>");

            slideNumber++;
        }

        if (slidesSb.Length == 0) return null;

        return $@"<div class=""extracted-pptx-slides"">
    <div class=""d-flex align-items-center justify-content-between mb-3 p-3 bg-light rounded-3 border"">
        <div class=""d-flex align-items-center gap-2"">
            <i class=""bi bi-file-earmark-slides-fill text-primary fs-4""></i>
            <div>
                <div class=""fw-bold text-body"">Презентация к лекции</div>
                <div class=""text-muted small"">Всего слайдов: {slideEntries.Count}. Вы можете изучать материал прямо на странице.</div>
            </div>
        </div>
        <span class=""badge bg-primary-subtle text-primary border"">{slideEntries.Count} слайдов</span>
    </div>
{slidesSb}
</div>";
    }

    private string? ExtractText(string fullDiskPath)
    {
        var rawText = File.ReadAllText(fullDiskPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        var paragraphs = rawText.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();

        foreach (var p in paragraphs)
        {
            var clean = WebUtility.HtmlEncode(p.Trim()).Replace("\r\n", "<br />").Replace("\n", "<br />");
            if (!string.IsNullOrWhiteSpace(clean))
            {
                sb.AppendLine($@"<p class=""lh-base text-body mb-3"">{clean}</p>");
            }
        }

        return $@"<div class=""extracted-text-document"">
{sb}
</div>";
    }
}
