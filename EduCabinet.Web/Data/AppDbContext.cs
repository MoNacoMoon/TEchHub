using Microsoft.EntityFrameworkCore;
using EduCabinet.Web.Models.Entities;

namespace EduCabinet.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<StudentGroup> StudentGroups => Set<StudentGroup>();
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
    public DbSet<Discipline> Disciplines => Set<Discipline>();
    public DbSet<DisciplineGroup> DisciplineGroups => Set<DisciplineGroup>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Lecture> Lectures => Set<Lecture>();
    public DbSet<LectureProgress> LectureProgresses => Set<LectureProgress>();
    public DbSet<ControlQuestion> ControlQuestions => Set<ControlQuestion>();
    public DbSet<ControlQuestionOption> ControlQuestionOptions => Set<ControlQuestionOption>();
    public DbSet<StudentControlAnswer> StudentControlAnswers => Set<StudentControlAnswer>();
    public DbSet<Test> Tests => Set<Test>();
    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();
    public DbSet<TestQuestionOption> TestQuestionOptions => Set<TestQuestionOption>();
    public DbSet<TestAttempt> TestAttempts => Set<TestAttempt>();
    public DbSet<TestAttemptAnswer> TestAttemptAnswers => Set<TestAttemptAnswer>();
    public DbSet<EventLog> EventLogs => Set<EventLog>();
    public DbSet<CourseAnnouncement> CourseAnnouncements => Set<CourseAnnouncement>();
    public DbSet<SystemInviteToken> SystemInviteTokens => Set<SystemInviteToken>();
    public DbSet<StudentReportSubmission> StudentReportSubmissions => Set<StudentReportSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        // GroupMembership
        modelBuilder.Entity<GroupMembership>()
            .HasOne(gm => gm.Student)
            .WithMany(u => u.GroupMemberships)
            .HasForeignKey(gm => gm.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GroupMembership>()
            .HasOne(gm => gm.Group)
            .WithMany(g => g.Memberships)
            .HasForeignKey(gm => gm.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // DisciplineGroup
        modelBuilder.Entity<DisciplineGroup>()
            .HasOne(dg => dg.Discipline)
            .WithMany(d => d.DisciplineGroups)
            .HasForeignKey(dg => dg.DisciplineId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DisciplineGroup>()
            .HasOne(dg => dg.Group)
            .WithMany(g => g.DisciplineGroups)
            .HasForeignKey(dg => dg.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Topic -> Discipline
        modelBuilder.Entity<Topic>()
            .HasOne(t => t.Discipline)
            .WithMany(d => d.Topics)
            .HasForeignKey(t => t.DisciplineId)
            .OnDelete(DeleteBehavior.Cascade);

        // Lecture -> Topic
        modelBuilder.Entity<Lecture>()
            .HasOne(l => l.Topic)
            .WithMany(t => t.Lectures)
            .HasForeignKey(l => l.TopicId)
            .OnDelete(DeleteBehavior.Cascade);

        // LectureProgress
        modelBuilder.Entity<LectureProgress>()
            .HasOne(lp => lp.Student)
            .WithMany(u => u.LectureProgresses)
            .HasForeignKey(lp => lp.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LectureProgress>()
            .HasOne(lp => lp.Lecture)
            .WithMany(l => l.Progresses)
            .HasForeignKey(lp => lp.LectureId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LectureProgress>()
            .HasIndex(lp => new { lp.StudentId, lp.LectureId })
            .IsUnique();

        // ControlQuestion -> Lecture
        modelBuilder.Entity<ControlQuestion>()
            .HasOne(cq => cq.Lecture)
            .WithMany(l => l.ControlQuestions)
            .HasForeignKey(cq => cq.LectureId)
            .OnDelete(DeleteBehavior.Cascade);

        // ControlQuestionOption -> ControlQuestion
        modelBuilder.Entity<ControlQuestionOption>()
            .HasOne(o => o.ControlQuestion)
            .WithMany(cq => cq.Options)
            .HasForeignKey(o => o.ControlQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // StudentControlAnswer
        modelBuilder.Entity<StudentControlAnswer>()
            .HasOne(sa => sa.Student)
            .WithMany(u => u.ControlAnswers)
            .HasForeignKey(sa => sa.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StudentControlAnswer>()
            .HasOne(sa => sa.ControlQuestion)
            .WithMany(cq => cq.StudentAnswers)
            .HasForeignKey(sa => sa.ControlQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Test -> Lecture (1 to 1)
        modelBuilder.Entity<Test>()
            .HasOne(t => t.Lecture)
            .WithOne(l => l.Test)
            .HasForeignKey<Test>(t => t.LectureId)
            .OnDelete(DeleteBehavior.Cascade);

        // TestQuestion -> Test
        modelBuilder.Entity<TestQuestion>()
            .HasOne(tq => tq.Test)
            .WithMany(t => t.Questions)
            .HasForeignKey(tq => tq.TestId)
            .OnDelete(DeleteBehavior.Cascade);

        // TestQuestionOption -> TestQuestion
        modelBuilder.Entity<TestQuestionOption>()
            .HasOne(o => o.TestQuestion)
            .WithMany(tq => tq.Options)
            .HasForeignKey(o => o.TestQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // TestAttempt
        modelBuilder.Entity<TestAttempt>()
            .HasOne(ta => ta.Student)
            .WithMany(u => u.TestAttempts)
            .HasForeignKey(ta => ta.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TestAttempt>()
            .HasOne(ta => ta.Test)
            .WithMany(t => t.Attempts)
            .HasForeignKey(ta => ta.TestId)
            .OnDelete(DeleteBehavior.Cascade);

        // TestAttemptAnswer
        modelBuilder.Entity<TestAttemptAnswer>()
            .HasOne(taa => taa.Attempt)
            .WithMany(ta => ta.Answers)
            .HasForeignKey(taa => taa.TestAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TestAttemptAnswer>()
            .HasOne(taa => taa.Question)
            .WithMany(tq => tq.AttemptAnswers)
            .HasForeignKey(taa => taa.TestQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // CourseAnnouncement
        modelBuilder.Entity<CourseAnnouncement>()
            .HasOne(a => a.Discipline)
            .WithMany(d => d.Announcements)
            .HasForeignKey(a => a.DisciplineId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseAnnouncement>()
            .HasOne(a => a.AuthorTeacher)
            .WithMany()
            .HasForeignKey(a => a.AuthorTeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        // StudentReportSubmission
        modelBuilder.Entity<StudentReportSubmission>()
            .HasOne(s => s.Lecture)
            .WithMany(l => l.ReportSubmissions)
            .HasForeignKey(s => s.LectureId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StudentReportSubmission>()
            .HasOne(s => s.Student)
            .WithMany(u => u.ReportSubmissions)
            .HasForeignKey(s => s.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StudentReportSubmission>()
            .HasOne(s => s.ReviewedByTeacher)
            .WithMany()
            .HasForeignKey(s => s.ReviewedByTeacherId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
