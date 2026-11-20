using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace JohnVGDCBot
{
    public class ReminderGroupMember
    {
        public int Id { get; set; }
        public int ReminderGroupId { get; set; }
        public ulong MemberId { get; set; }
        public bool IsRole { get; set; }
        public ReminderGroup ReminderGroup { get; set; } = null!;
    }
    public class ReminderGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public ulong GuildId { get; set; }
        public ulong CreatorId { get; set; }
        public List<ReminderGroupMember> Members { get; set; } = [];
    }

    public class KeyHolding
    {
        public int Id { get; set; }
        public ulong GuildId { get; set; }
        public ulong HolderID { get; set; }
        public DateTime AcquiredAt { get; set; }
        public DateTime? ReleasedAt { get; set; }
        public ulong RecordedById { get; set; }
    }

    public class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
    {
        public DbSet<ReminderGroup> ReminderGroups => Set<ReminderGroup>();
        public DbSet<ReminderGroupMember> ReminderGroupMembers => Set<ReminderGroupMember>();
        public DbSet<KeyHolding> KeyHoldings => Set<KeyHolding>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ReminderGroupMember>()
                .Property(m => m.MemberId)
                .HasConversion<string>();
            modelBuilder.Entity<ReminderGroup>()
                .Property(m => m.GuildId)
                .HasConversion<string>();

            modelBuilder.Entity<KeyHolding>()
                .HasIndex(h => new { h.GuildId, h.HolderID })
                .IsUnique()
                .HasFilter("ReleasedAt IS NULL");
            modelBuilder.Entity<KeyHolding>()
                .Property(m => m.GuildId)
                .HasConversion<string>();
            modelBuilder.Entity<KeyHolding>()
                .Property(m => m.HolderID)
                .HasConversion<string>();
            modelBuilder.Entity<KeyHolding>()
                .Property(m => m.RecordedById)
                .HasConversion<string>();
            modelBuilder.Entity<KeyHolding>()
                .Property(m => m.ReleasedAt)
                .IsConcurrencyToken();
        }
    }
}
