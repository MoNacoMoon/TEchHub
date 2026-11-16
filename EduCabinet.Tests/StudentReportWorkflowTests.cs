using Xunit;
using EduCabinet.Web.Data;
using EduCabinet.Web.Models.Entities;
using EduCabinet.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace EduCabinet.Tests;

public class StudentReportWorkflowTests
{
    [Fact]
    public async Task LectureRequiresReport_NotCompleted_WhenReportNotSubmitted()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var student = new User { Username = "student_rep1", FullName = "Петров П.П.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Архитектура ЭВМ", Description = "Практический курс" };
            var topic = new Topic { Discipline = discipline, Title = "Тема 1. Лабораторные работы", OrderIndex = 1 };
            var lecture = new Lecture
            {
                Topic = topic,
                Title = "Лабораторная работа №1",
                OrderIndex = 1,
                ContentHtml = "Методические указания",
                RequiresReportSubmission = true,
                MaxReportScore = 100
            };

            context.Users.Add(student);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.Add(lecture);
            await context.SaveChangesAsync();

            // Act
            bool isCompleted = await service.UpdateLectureCompletionStatusAsync(student.Id, lecture.Id);
            var progress = await context.LectureProgresses.FirstOrDefaultAsync(p => p.StudentId == student.Id && p.LectureId == lecture.Id);

            // Assert
            Assert.False(isCompleted);
            Assert.NotNull(progress);
            Assert.False(progress.IsReportApproved);
            Assert.False(progress.IsCompleted);
        }
    }

    [Fact]
    public async Task LectureRequiresReport_RemainsLocked_WhenReportIsPendingOrNeedsRevision()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var student = new User { Username = "student_rep2", FullName = "Сидоров С.С.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Операционные системы", Description = "Лабораторный практикум" };
            var topic = new Topic { Discipline = discipline, Title = "Тема 1", OrderIndex = 1 };
            var lec1 = new Lecture
            {
                Topic = topic,
                Title = "Лаб 1. Процессы",
                OrderIndex = 1,
                ContentHtml = "Теория",
                RequiresReportSubmission = true
            };
            var lec2 = new Lecture
            {
                Topic = topic,
                Title = "Лаб 2. Потоки",
                OrderIndex = 2,
                ContentHtml = "Теория 2"
            };

            context.Users.Add(student);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.AddRange(lec1, lec2);
            await context.SaveChangesAsync();

            // 1. Студент отправил отчет (статус Pending)
            var submission = new StudentReportSubmission
            {
                LectureId = lec1.Id,
                StudentId = student.Id,
                FilePath = "/uploads/reports/rep_1.docx",
                OriginalFileName = "Lab1_Report.docx",
                FileSizeBytes = 10240,
                Status = ReportSubmissionStatus.Pending,
                SubmittedAt = DateTime.UtcNow
            };
            context.StudentReportSubmissions.Add(submission);
            await context.SaveChangesAsync();

            await service.UpdateLectureCompletionStatusAsync(student.Id, lec1.Id);

            bool canAccessLec2WhilePending = await service.CanStudentAccessLectureAsync(student.Id, lec2.Id);
            Assert.False(canAccessLec2WhilePending, "Лекция 2 должна быть заблокирована, пока отчет по лекции 1 на проверке");

            // 2. Преподаватель отправил отчет на доработку (статус NeedsRevision)
            submission.Status = ReportSubmissionStatus.NeedsRevision;
            submission.TeacherFeedback = "Исправьте выводы в разделе 3 и оформите титульный лист.";
            await context.SaveChangesAsync();

            await service.UpdateLectureCompletionStatusAsync(student.Id, lec1.Id);

            bool canAccessLec2WhileRevision = await service.CanStudentAccessLectureAsync(student.Id, lec2.Id);
            Assert.False(canAccessLec2WhileRevision, "Лекция 2 должна оставаться заблокированной при статусе 'На доработке'");
        }
    }

    [Fact]
    public async Task LectureRequiresReport_CompletesAndUnlocksNextLecture_WhenReportIsApproved()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var student = new User { Username = "student_rep3", FullName = "Кузнецов К.К.", Role = UserRole.Student };
            var teacher = new User { Username = "teacher1", FullName = "Профессор Преображенский", Role = UserRole.Teacher };
            var discipline = new Discipline { Title = "Базы данных", Description = "Лабораторные работы" };
            var topic = new Topic { Discipline = discipline, Title = "Модуль 1", OrderIndex = 1 };
            var lec1 = new Lecture
            {
                Topic = topic,
                Title = "Лабораторная работа 1",
                OrderIndex = 1,
                ContentHtml = "Задание",
                RequiresReportSubmission = true,
                MaxReportScore = 100
            };
            var lec2 = new Lecture
            {
                Topic = topic,
                Title = "Лабораторная работа 2",
                OrderIndex = 2,
                ContentHtml = "Задание 2",
                RequiresReportSubmission = true
            };

            context.Users.AddRange(student, teacher);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.AddRange(lec1, lec2);
            await context.SaveChangesAsync();

            // Создаем проверенный и зачтенный отчет
            var approvedSubmission = new StudentReportSubmission
            {
                LectureId = lec1.Id,
                StudentId = student.Id,
                FilePath = "/uploads/reports/rep_kuznetsov.pdf",
                OriginalFileName = "Database_Lab1.pdf",
                FileSizeBytes = 20480,
                Status = ReportSubmissionStatus.Approved,
                Grade = 95,
                TeacherFeedback = "Отличная схема и оптимизированные запросы!",
                ReviewedAt = DateTime.UtcNow,
                ReviewedByTeacherId = teacher.Id
            };
            context.StudentReportSubmissions.Add(approvedSubmission);
            await context.SaveChangesAsync();

            // Act: пересчет статуса
            bool isCompleted = await service.UpdateLectureCompletionStatusAsync(student.Id, lec1.Id);
            var progress = await context.LectureProgresses.FirstOrDefaultAsync(p => p.StudentId == student.Id && p.LectureId == lec1.Id);
            bool canAccessLec2 = await service.CanStudentAccessLectureAsync(student.Id, lec2.Id);

            // Assert
            Assert.True(isCompleted, "Лекция 1 должна быть завершена после одобрения отчета");
            Assert.NotNull(progress);
            Assert.True(progress.IsCompleted);
            Assert.True(progress.IsReportApproved);
            Assert.Equal(95, progress.ReportGrade);
            Assert.True(canAccessLec2, "Лекция 2 должна успешно разблокироваться для студента");
        }
    }

    [Fact]
    public async Task LectureWithoutReport_CompletesNormally_WithoutReportSubmission()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var student = new User { Username = "student_rep4", FullName = "Смирнов А.А.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Введение в специальность", Description = "Лекционный курс" };
            var topic = new Topic { Discipline = discipline, Title = "Вводная часть", OrderIndex = 1 };
            var lec1 = new Lecture
            {
                Topic = topic,
                Title = "Вводная лекция",
                OrderIndex = 1,
                ContentHtml = "Общий обзор",
                RequiresReportSubmission = false // Не требует отчета
            };

            context.Users.Add(student);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.Add(lec1);
            await context.SaveChangesAsync();

            // Act
            bool isCompleted = await service.UpdateLectureCompletionStatusAsync(student.Id, lec1.Id);
            var progress = await context.LectureProgresses.FirstOrDefaultAsync(p => p.StudentId == student.Id && p.LectureId == lec1.Id);

            // Assert
            Assert.True(isCompleted);
            Assert.NotNull(progress);
            Assert.True(progress.IsCompleted);
            Assert.True(progress.IsReportApproved);
        }
    }

    [Fact]
    public async Task StudentCanUpdateSubmission_WhenNeedsRevision()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var student = new User { Username = "student_rep5", FullName = "Морозов М.М.", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Информационная безопасность", Description = "Курс" };
            var topic = new Topic { Discipline = discipline, Title = "Криптография", OrderIndex = 1 };
            var lecture = new Lecture
            {
                Topic = topic,
                Title = "Лабораторная по RSA",
                OrderIndex = 1,
                ContentHtml = "Задание",
                RequiresReportSubmission = true
            };

            context.Users.Add(student);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.Add(lecture);
            await context.SaveChangesAsync();

            var initialSubmission = new StudentReportSubmission
            {
                LectureId = lecture.Id,
                StudentId = student.Id,
                FilePath = "/uploads/reports/v1.docx",
                OriginalFileName = "Report_v1.docx",
                Status = ReportSubmissionStatus.NeedsRevision,
                TeacherFeedback = "Добавьте пример дешифрования",
                Grade = null
            };
            context.StudentReportSubmissions.Add(initialSubmission);
            await context.SaveChangesAsync();

            // Имитируем отправку обновленного файла студентом
            var existing = await context.StudentReportSubmissions
                .FirstOrDefaultAsync(s => s.LectureId == lecture.Id && s.StudentId == student.Id);

            Assert.NotNull(existing);
            existing.FilePath = "/uploads/reports/v2.docx";
            existing.OriginalFileName = "Report_v2_fixed.docx";
            existing.Status = ReportSubmissionStatus.Pending;
            existing.StudentComment = "Исправил, добавил расчет ключа d и шаг дешифрования.";
            existing.TeacherFeedback = null;
            existing.ReviewedAt = null;
            await context.SaveChangesAsync();

            var updated = await context.StudentReportSubmissions.FindAsync(existing.Id);
            Assert.NotNull(updated);
            Assert.Equal(ReportSubmissionStatus.Pending, updated.Status);
            Assert.Equal("Report_v2_fixed.docx", updated.OriginalFileName);
            Assert.Null(updated.TeacherFeedback);
        }
    }

    [Fact]
    public async Task TeacherCanAttachReviewedFile_StudentReceivesIt()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var teacher = new User { Username = "prof_brown", FullName = "Профессор Браун", Role = UserRole.Teacher };
            var student = new User { Username = "student_doc", FullName = "Марти Макфлай", Role = UserRole.Student };
            var discipline = new Discipline { Title = "Физика времени", Description = "Практический курс" };
            var topic = new Topic { Discipline = discipline, Title = "Лабораторные", OrderIndex = 1 };
            var lecture = new Lecture
            {
                Topic = topic,
                Title = "Лабораторная по потоковому накопителю",
                OrderIndex = 1,
                ContentHtml = "Методичка",
                RequiresReportSubmission = true,
                MaxReportScore = 100
            };

            context.Users.AddRange(teacher, student);
            context.Disciplines.Add(discipline);
            context.Topics.Add(topic);
            context.Lectures.Add(lecture);
            await context.SaveChangesAsync();

            // 1. Студент сдал работу
            var submission = new StudentReportSubmission
            {
                LectureId = lecture.Id,
                StudentId = student.Id,
                FilePath = "/uploads/reports/flux_report.docx",
                OriginalFileName = "Flux_Report.docx",
                FileSizeBytes = 15000,
                Status = ReportSubmissionStatus.Pending,
                SubmittedAt = DateTime.UtcNow
            };
            context.StudentReportSubmissions.Add(submission);
            await context.SaveChangesAsync();

            // 2. Преподаватель проверил, оставил замечания и прикрепил файл с правками
            submission.Status = ReportSubmissionStatus.NeedsRevision;
            submission.Grade = 60;
            submission.TeacherFeedback = "Исправьте расчет мощности в 1.21 ГВт. См. файл с правками.";
            submission.ReviewedAt = DateTime.UtcNow;
            submission.ReviewedByTeacherId = teacher.Id;
            submission.ReviewedFilePath = "/uploads/reports/t_rev_flux_corrections.docx";
            submission.ReviewedFileName = "Flux_Report_с_правками.docx";
            submission.ReviewedFileSizeBytes = 18500;

            await context.SaveChangesAsync();

            // Assert
            var saved = await context.StudentReportSubmissions
                .Include(s => s.ReviewedByTeacher)
                .FirstOrDefaultAsync(s => s.Id == submission.Id);

            Assert.NotNull(saved);
            Assert.Equal(ReportSubmissionStatus.NeedsRevision, saved.Status);
            Assert.Equal(60, saved.Grade);
            Assert.Equal("/uploads/reports/t_rev_flux_corrections.docx", saved.ReviewedFilePath);
            Assert.Equal("Flux_Report_с_правками.docx", saved.ReviewedFileName);
            Assert.Equal(18500, saved.ReviewedFileSizeBytes);
            Assert.Equal("Профессор Браун", saved.ReviewedByTeacher?.FullName);
        }
    }

    [Fact]
    public async Task StudentGradebook_AggregatesResultsCorrectly()
    {
        var (context, connection) = TestHelper.CreateInMemoryDbContext();
        using (connection)
        using (context)
        {
            var eventLogger = new EventLogService(context);
            var service = new LectureAccessService(context, eventLogger);

            var student = new User { Username = "student_gb", FullName = "Иванов Иван", Role = UserRole.Student };
            var group = new StudentGroup { Name = "ИТ-21" };
            var discipline = new Discipline { Title = "Программирование", Description = "Базовый курс" };
            var topic = new Topic { Discipline = discipline, Title = "Тема 1", OrderIndex = 1 };
            var lec1 = new Lecture { Topic = topic, Title = "Лекция 1", OrderIndex = 1, ContentHtml = "Теория 1", RequiresReportSubmission = true, MaxReportScore = 100 };
            var lec2 = new Lecture { Topic = topic, Title = "Лекция 2", OrderIndex = 2, ContentHtml = "Теория 2" };

            context.Users.Add(student);
            context.StudentGroups.Add(group);
            context.GroupMemberships.Add(new GroupMembership { Student = student, Group = group });
            context.Disciplines.Add(discipline);
            context.DisciplineGroups.Add(new DisciplineGroup { Discipline = discipline, Group = group });
            context.Topics.Add(topic);
            context.Lectures.AddRange(lec1, lec2);
            await context.SaveChangesAsync();

            // Студент сдал отчет по лекции 1 и преподаватель его зачел
            var report = new StudentReportSubmission
            {
                LectureId = lec1.Id,
                StudentId = student.Id,
                FilePath = "/uploads/reports/code.zip",
                OriginalFileName = "code.zip",
                Status = ReportSubmissionStatus.Approved,
                Grade = 95,
                ReviewedAt = DateTime.UtcNow
            };
            context.StudentReportSubmissions.Add(report);
            await context.SaveChangesAsync();

            await service.UpdateLectureCompletionStatusAsync(student.Id, lec1.Id);

            var prog = await service.GetDisciplineProgressAsync(student.Id, discipline.Id);

            Assert.Equal(2, prog.TotalLectures);
            Assert.Equal(1, prog.CompletedLectures);
            Assert.Equal(50, prog.ProgressPercent);

            var lec1Info = prog.Lectures.First(l => l.LectureId == lec1.Id);
            Assert.True(lec1Info.IsReportApproved);
            Assert.Equal(95, lec1Info.ReportGrade);
        }
    }
}
