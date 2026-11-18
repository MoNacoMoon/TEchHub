using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;

namespace EduCabinet.Tests;

public class DocumentExtractorAndLectureCompletionTests
{
    [Fact]
    public void DocumentExtractor_ReturnsNull_ForNonExistentFile()
    {
        var extractor = new DocumentContentExtractorService(NullLogger<DocumentContentExtractorService>.Instance);
        var result = extractor.ExtractContentHtml("C:\\non_existent_folder_xyz\\file.docx", "file.docx");
        Assert.Null(result);
    }

    [Fact]
    public void DocumentExtractor_ReturnsNull_ForUnsupportedExtension()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var extractor = new DocumentContentExtractorService(NullLogger<DocumentContentExtractorService>.Instance);
            var result = extractor.ExtractContentHtml(tempFile, "program.exe");
            Assert.Null(result);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DocumentExtractor_ExtractsAndCaches_TextFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "educabinet_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "notes.txt");
        var cacheFile = tempFile + ".extracted.html";

        try
        {
            File.WriteAllText(tempFile, "Первый параграф лекции.\n\nВторой параграф с важными данными.", Encoding.UTF8);

            var extractor = new DocumentContentExtractorService(NullLogger<DocumentContentExtractorService>.Instance);
            var result = extractor.ExtractContentHtml(tempFile, "notes.txt");

            Assert.NotNull(result);
            Assert.Contains("Первый параграф лекции", result);
            Assert.Contains("Второй параграф с важными данными", result);
            Assert.True(File.Exists(cacheFile));

            // Проверяем чтение из кэша
            var cachedResult = extractor.ExtractContentHtml(tempFile, "notes.txt");
            Assert.Equal(result, cachedResult);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DocumentExtractor_Extracts_DocxDocument()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "educabinet_docx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "lecture.docx");

        try
        {
            // Создаем минимальный валидный docx в формате zip
            using (var zipStream = new FileStream(tempFile, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("word/document.xml");
                using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                {
                    writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body>
    <w:p>
      <w:pPr><w:pStyle w:val=""Heading1"" /></w:pPr>
      <w:r><w:t>Теоретический раздел 1</w:t></w:r>
    </w:p>
    <w:p>
      <w:r><w:rPr><w:b/></w:rPr><w:t>Важное определение в полужирном начертании</w:t></w:r>
    </w:p>
  </w:body>
</w:document>");
                }
            }

            var extractor = new DocumentContentExtractorService(NullLogger<DocumentContentExtractorService>.Instance);
            var result = extractor.ExtractContentHtml(tempFile, "lecture.docx");

            Assert.NotNull(result);
            Assert.Contains("Теоретический раздел 1", result);
            Assert.Contains("<strong>Важное определение в полужирном начертании</strong>", result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DocumentExtractor_Extracts_PptxPresentationSlides()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "educabinet_pptx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "presentation.pptx");

        try
        {
            // Создаем минимальный валидный pptx в формате zip
            using (var zipStream = new FileStream(tempFile, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var entry1 = archive.CreateEntry("ppt/slides/slide1.xml");
                using (var writer = new StreamWriter(entry1.Open(), Encoding.UTF8))
                {
                    writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:sld xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"">
  <p:cSld>
    <p:spTree>
      <p:sp>
        <p:txBody>
          <a:p><a:r><a:t>Введение в архитектуру систем</a:t></a:r></a:p>
          <a:p><a:r><a:t>Ключевые принципы SOLID</a:t></a:r></a:p>
          <a:p><a:r><a:t>Паттерны проектирования GoF</a:t></a:r></a:p>
        </p:txBody>
      </p:sp>
    </p:spTree>
  </p:cSld>
</p:sld>");
                }
            }

            var extractor = new DocumentContentExtractorService(NullLogger<DocumentContentExtractorService>.Instance);
            var result = extractor.ExtractContentHtml(tempFile, "presentation.pptx");

            Assert.NotNull(result);
            Assert.Contains("Введение в архитектуру систем", result);
            Assert.Contains("Ключевые принципы SOLID", result);
            Assert.Contains("Паттерны проектирования GoF", result);
            Assert.Contains("Слайд 1 из 1", result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Lecture_WithoutQuestionsAndTest_AutoCompletesSuccessfully()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var user = new User { Username = "student_reader", FullName = "Петров П.П.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Информационные технологии", Description = "Базовый курс" };
            var topic = new Topic { Discipline = discipline, Title = "Раздел 1", OrderIndex = 1 };

            // Лекция 1: пустой автотест с 0 вопросов, без контрольных вопросов, без отчета
            var lec1 = new Lecture { Topic = topic, Title = "Вводная лекция", OrderIndex = 1, ContentHtml = "<p>Материал</p>" };
            var emptyTest = new Test { Lecture = lec1, Title = "Пустой тест", IsActive = true, PassingScorePercent = 70 };

            // Лекция 2
            var lec2 = new Lecture { Topic = topic, Title = "Вторая лекция", OrderIndex = 2, ContentHtml = "<p>Тема 2</p>" };

            context.Users.Add(user);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.AddRange(lec1, lec2);
            context.Tests.Add(emptyTest);
            await context.SaveChangesAsync();

            // Проверяем завершение лекции
            bool isCompleted = await service.UpdateLectureCompletionStatusAsync(user.Id, lec1.Id);

            // Лекция 1 успешно считается завершенной, так как вопросов нет
            Assert.True(isCompleted);

            // Лекция 2 теперь разблокирована и доступна студенту!
            var statusLec2 = await service.GetLectureStatusAsync(user.Id, lec2.Id);
            Assert.Equal(LectureAccessStatus.Available, statusLec2);
        }
    }
}
