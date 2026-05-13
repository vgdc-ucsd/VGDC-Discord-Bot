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
        public List<ReminderGroupMember> Members { get; set; } = [];
    }

    public class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
    {
        public DbSet<ReminderGroup> ReminderGroups => Set<ReminderGroup>();
        public DbSet<ReminderGroupMember> ReminderGroupMembers => Set<ReminderGroupMember>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ReminderGroupMember>()
                .Property(m => m.MemberId)
                .HasConversion<string>();
            modelBuilder.Entity<ReminderGroup>()
                .Property(m => m.GuildId)
                .HasConversion<string>();
        }
    }
}
