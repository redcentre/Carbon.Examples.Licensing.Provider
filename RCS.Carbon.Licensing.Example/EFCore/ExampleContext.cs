using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace RCS.Carbon.Licensing.Example.EFCore;

public partial class ExampleContext : DbContext
{
    public ExampleContext(DbContextOptions<ExampleContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<Job> Jobs { get; set; }

    public virtual DbSet<Realm> Realms { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_Customer_Id");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_Job_Id");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Customer).WithMany(p => p.Jobs).HasConstraintName("FK_Job_CustomerId");
        });

        modelBuilder.Entity<Realm>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_Realm_Id");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");

            entity.HasMany(d => d.Customers).WithMany(p => p.Realms)
                .UsingEntity<Dictionary<string, object>>(
                    "RealmCustomer",
                    r => r.HasOne<Customer>().WithMany()
                        .HasForeignKey("CustomerId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_RealmCustomer_CustomerId"),
                    l => l.HasOne<Realm>().WithMany()
                        .HasForeignKey("RealmId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_RealmCustomer_RealmId"),
                    j =>
                    {
                        j.HasKey("RealmId", "CustomerId");
                        j.ToTable("RealmCustomer");
                    });

            entity.HasMany(d => d.Users).WithMany(p => p.Realms)
                .UsingEntity<Dictionary<string, object>>(
                    "RealmUser",
                    r => r.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_RealmUser_UserId"),
                    l => l.HasOne<Realm>().WithMany()
                        .HasForeignKey("RealmId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_RealmUser_RealmId"),
                    j =>
                    {
                        j.HasKey("RealmId", "UserId");
                        j.ToTable("RealmUser");
                    });
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_User_Id");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Created).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Uid).HasDefaultValueSql("(newid())");

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
