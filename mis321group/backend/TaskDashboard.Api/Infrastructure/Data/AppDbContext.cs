using Microsoft.EntityFrameworkCore;
using TaskDashboard.Api.Domain.Entities;

namespace TaskDashboard.Api.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Scaffold sets are intentionally small for now; fields will grow with features.
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Relationships (Project -> Tasks)
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired();
            entity.Property(p => p.Category).IsRequired();

            entity.HasMany(p => p.Tasks)
                .WithOne(t => t.Project)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Title).IsRequired();
            entity.Property(t => t.CreatedAt).IsRequired();
        });

        // Seed sample data (fixed timestamps so migrations are deterministic).
        var project1Created = new DateTime(2026, 3, 23, 9, 0, 0, DateTimeKind.Utc);
        var project2Created = new DateTime(2026, 3, 23, 9, 30, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Project>().HasData(
            new Project
            {
                Id = 1,
                Name = "School MIS321",
                Description = "Group coursework and personal progress tracking.",
                Category = "Education"
            },
            new Project
            {
                Id = 2,
                Name = "Personal Admin",
                Description = "Small ongoing tasks to keep life running smoothly.",
                Category = "Personal"
            }
        );

        modelBuilder.Entity<TaskItem>().HasData(
            new TaskItem
            {
                Id = 1,
                Title = "Draft project plan",
                Description = "Outline goals, milestones, and dashboard structure.",
                Priority = TaskPriority.Medium,
                DueDate = new DateTime(2026, 3, 25, 17, 0, 0, DateTimeKind.Utc),
                Status = TaskItemStatus.Todo,
                ProjectId = 1,
                CreatedAt = project1Created
            },
            new TaskItem
            {
                Id = 2,
                Title = "Complete API scaffolding",
                Description = "Implement DbContext, migrations, and basic endpoints.",
                Priority = TaskPriority.High,
                DueDate = new DateTime(2026, 3, 26, 17, 0, 0, DateTimeKind.Utc),
                Status = TaskItemStatus.InProgress,
                ProjectId = 1,
                CreatedAt = project1Created.AddMinutes(15)
            },
            new TaskItem
            {
                Id = 3,
                Title = "Set up weekly review",
                Description = "Use dashboard to manage personal recurring tasks.",
                Priority = TaskPriority.Low,
                DueDate = null,
                Status = TaskItemStatus.Todo,
                ProjectId = 2,
                CreatedAt = project2Created
            }
        );
    }
}
