using System;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace KCTools.LicenseServer.Data
{
    public sealed class LicenseDbContext : IdentityDbContext<AppUser, AppRole, Guid>
    {
        public LicenseDbContext(DbContextOptions<LicenseDbContext> options)
            : base(options)
        {
        }


        public DbSet<License> Licenses => Set<License>();

        public DbSet<Device> Devices => Set<Device>();

        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        public DbSet<DeviceChallenge> DeviceChallenges => Set<DeviceChallenge>();

        public DbSet<ActivationHistory> ActivationHistory => Set<ActivationHistory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseOpenIddict();

            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.Property(item => item.DisplayName).HasMaxLength(128);
            });

            modelBuilder.Entity<License>(entity =>
            {
                entity.ToTable("Licenses");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Key).HasMaxLength(32).IsRequired();
                entity.HasIndex(item => item.Key).IsUnique();
                entity.Property(item => item.Plan).HasMaxLength(32).IsRequired();
                entity.Property(item => item.MaxDevices).HasDefaultValue(1);
                entity.HasOne(item => item.User)
                    .WithMany(user => user.Licenses)
                    .HasForeignKey(item => item.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Device>(entity =>
            {
                entity.ToTable("Devices");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Hwid).HasMaxLength(128).IsRequired();
                entity.Property(item => item.MachineName).HasMaxLength(128);
                entity.HasIndex(item => new { item.LicenseId, item.Hwid }).IsUnique();
                entity.HasIndex(item => item.DeviceId);
                entity.HasIndex(item => item.KeyThumbprint);
                entity.HasOne(item => item.License)
                    .WithMany(license => license.Devices)
                    .HasForeignKey(item => item.LicenseId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.ToTable("RefreshTokens");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.TokenHash).HasMaxLength(128).IsRequired();
                entity.HasIndex(item => item.TokenHash).IsUnique();
                entity.HasIndex(item => item.UserId);
                entity.HasOne(item => item.User)
                    .WithMany(user => user.RefreshTokens)
                    .HasForeignKey(item => item.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<DeviceChallenge>(entity =>
            {
                entity.ToTable("DeviceChallenges");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.NonceHash).HasMaxLength(128).IsRequired();
                entity.HasIndex(item => new { item.LicenseId, item.DeviceId, item.NonceHash });
                entity.HasIndex(item => item.ExpiresAt);
            });

            modelBuilder.Entity<ActivationHistory>(entity =>
            {
                entity.ToTable("ActivationHistory");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Hwid).HasMaxLength(128).IsRequired();
                entity.Property(item => item.Action).HasMaxLength(64).IsRequired();
                entity.Property(item => item.Actor).HasMaxLength(128).IsRequired();
                entity.Property(item => item.Details).HasMaxLength(2048);
                entity.HasIndex(item => item.LicenseId);
                entity.HasIndex(item => item.CreatedAt);
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Actor).HasMaxLength(128).IsRequired();
                entity.Property(item => item.Action).HasMaxLength(64).IsRequired();
                entity.Property(item => item.EntityType).HasMaxLength(64);
                entity.Property(item => item.EntityId).HasMaxLength(128);
                entity.Property(item => item.Details).HasMaxLength(2048);
                entity.Property(item => item.IpAddress).HasMaxLength(64);
                entity.HasIndex(item => item.CreatedAt);
            });
        }
    }
}
