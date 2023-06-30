using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace RCS.Carbon.Licensing.Example.EFCore;

public partial class ExampleContext : DbContext
{
	// Server=tcp:ortho-server-1.database.windows.net,1433;Initial Catalog=CarbonExample;Persist Security Info=False;User ID=greg;Password=M0ggies9;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
	readonly string _connect;

    public ExampleContext(string adoConnect)
    {
		_connect = adoConnect;
    }

    //public CarbonExampleContext(DbContextOptions<CarbonExampleContext> options)
    //    : base(options)
    //{
    //}

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<Job> Jobs { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer(_connect);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_Customer_Id");
            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_Job_Id");
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasOne(d => d.Customer).WithMany(p => p.Jobs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Job_CustomerId");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_User_Id");
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasMany(d => d.Customers).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserCustomer",
                    r => r.HasOne<Customer>().WithMany()
                        .HasForeignKey("CustomerId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_UserCustomer_CustomerId"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_UserCustomer_UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "CustomerId");
                        j.ToTable("UserCustomer");
                    });
            entity.HasMany(d => d.Jobs).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserJob",
                    r => r.HasOne<Job>().WithMany()
                        .HasForeignKey("JobId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_UserJob_JobId"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_UserJob_UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "JobId");
                        j.ToTable("UserJob");
                    });
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
