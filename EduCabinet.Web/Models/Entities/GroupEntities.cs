namespace EduCabinet.Web.Models.Entities;

public class StudentGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public int CourseNumber { get; set; } = 1;
    public int StudyDurationYears { get; set; } = 4;
    public int AdmissionYear { get; set; } = 2026;

    public ICollection<GroupMembership> Memberships { get; set; } = new List<GroupMembership>();
    public ICollection<DisciplineGroup> DisciplineGroups { get; set; } = new List<DisciplineGroup>();
}

public class GroupMembership
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;

    public int GroupId { get; set; }
    public StudentGroup Group { get; set; } = null!;
}

public class DisciplineGroup
{
    public int Id { get; set; }
    public int DisciplineId { get; set; }
    public Discipline Discipline { get; set; } = null!;

    public int GroupId { get; set; }
    public StudentGroup Group { get; set; } = null!;
}
