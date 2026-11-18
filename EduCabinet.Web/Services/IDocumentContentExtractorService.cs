namespace EduCabinet.Web.Services;

public interface IDocumentContentExtractorService
{
    /// <summary>
    /// Извлекает текстовый контент и форматирование из прикрепленного файла (.docx, .pptx, .txt, .md) в виде безопасного HTML.
    /// Возвращает null, если файл не поддерживается или не содержит извлекаемого текста.
    /// </summary>
    string? ExtractContentHtml(string fullDiskPath, string? originalFileName);
}
