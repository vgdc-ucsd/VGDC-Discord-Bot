using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;
using Quartz;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Data.SQLite;
using NetCord.Rest;
using NetCord;
using NetCord.Gateway;
using Microsoft.EntityFrameworkCore;

namespace JohnVGDCBot
{
    public static class VGDCColors
    {
        public static readonly Color Teal = new(0x267392);
        public static readonly Color Cerulean = new(0x3C99AA);
        public static readonly Color Aqua = new(0x0EB9AE);
        public static readonly Color Mint = new(0x50D0A1);
    }
    
    public partial class ReminderModule(ISchedulerFactory schedulerFactory, BotDbContext db, GatewayClient client) 
        : ApplicationCommandModule<ApplicationCommandContext>
    {
        public record struct ReminderJobData(
            string Title,
            ulong GuildId, 
            ulong ChannelId, 
            ulong CreatorId, 
            string? UserIdString,
            string? GroupName, 
            string? Message)
        {
            public ReminderJobData(JobDataMap data) : this(
                    data.GetString("title") ?? throw new InvalidOperationException("title missing"),
                    ulong.Parse(data.GetString("guildId") ?? throw new InvalidOperationException("guildId missing")),
                    ulong.Parse(data.GetString("channelId") ?? throw new InvalidOperationException("channelId missing")),
                    ulong.Parse(data.GetString("creatorId") ?? throw new InvalidOperationException("creatorId missing")),
                    data.GetString("userId"),
                    data.GetString("group"),
                    data.GetString("message")
                ) {}
        }

        public enum Month
        {
            January = 1,
            February,
            March,
            April,
            May,
            June,
            July,
            August,
            September,
            October,
            November,
            December,
        };

        public enum Frequency
        {
            [SlashCommandChoice(Name = "One Time")] OneTime,
            [SlashCommandChoice(Name = "Every 5 minutes")] Every5Minutes,
            [SlashCommandChoice(Name = "Every 10 minutes")] Every10Minutes,
            [SlashCommandChoice(Name = "Every 15 minutes")] Every15Minutes,
            [SlashCommandChoice(Name = "Every 30 minutes")] Every30Minutes,
            [SlashCommandChoice(Name = "Every 45 minutes")] Every45Minutes,
            [SlashCommandChoice(Name = "Every hour")] EveryHour,
            [SlashCommandChoice(Name = "Every day")] EveryDay,
            [SlashCommandChoice(Name = "Every 2 days")] Every2Days,
            [SlashCommandChoice(Name = "Every 3 days")] Every3Days,
            [SlashCommandChoice(Name = "Every 4 days")] Every4Days,
            [SlashCommandChoice(Name = "Every 5 days")] Every5Days,
            [SlashCommandChoice(Name = "Every 6 days")] Every6Days,
            [SlashCommandChoice(Name = "Every week")] EveryWeek,
            [SlashCommandChoice(Name = "Every 2 weeks")] Every2Weeks,
            [SlashCommandChoice(Name = "Every 3 weeks")] Every3Weeks,
            [SlashCommandChoice(Name = "Every month")] EveryMonth,
            [SlashCommandChoice(Name = "Every 2 months")] Every2Months,
            [SlashCommandChoice(Name = "Every 3 months")] Every3Months,
            [SlashCommandChoice(Name = "Every year")] EveryYear,
        };

        private CalendarIntervalScheduleBuilder GetScheduleWithInterval(CalendarIntervalScheduleBuilder b, Frequency frequency)
        {
            return frequency switch
            {
                Frequency.Every5Minutes => b.WithIntervalInMinutes(5),
                Frequency.Every10Minutes => b.WithIntervalInMinutes(10),
                Frequency.Every15Minutes => b.WithIntervalInMinutes(15),
                Frequency.Every30Minutes => b.WithIntervalInMinutes(30),
                Frequency.Every45Minutes => b.WithIntervalInMinutes(45),
                Frequency.EveryHour => b.WithIntervalInHours(1),
                Frequency.EveryDay => b.WithIntervalInDays(1),
                Frequency.Every2Days => b.WithIntervalInDays(2),
                Frequency.Every3Days => b.WithIntervalInDays(3),
                Frequency.Every4Days => b.WithIntervalInDays(4),
                Frequency.Every5Days => b.WithIntervalInDays(5),
                Frequency.Every6Days => b.WithIntervalInDays(6),
                Frequency.EveryWeek => b.WithIntervalInWeeks(1),
                Frequency.Every2Weeks => b.WithIntervalInWeeks(2),
                Frequency.Every3Weeks => b.WithIntervalInWeeks(3),
                Frequency.EveryMonth => b.WithIntervalInMonths(1),
                Frequency.Every2Months => b.WithIntervalInMonths(2),
                Frequency.Every3Months => b.WithIntervalInMonths(3),
                Frequency.EveryYear => b.WithIntervalInYears(1),
                _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
            };
        }

        public class YearAutocompleteProvider() : IAutocompleteProvider<AutocompleteInteractionContext>
        {
            private const int maxYearsFromNow = 4;

            public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
                ApplicationCommandInteractionDataOption option, 
                AutocompleteInteractionContext context)
            {
                int currentYear = DateTime.UtcNow.Year;
                var choices = Enumerable.Range(currentYear, maxYearsFromNow)
                    .Select(year => new ApplicationCommandOptionChoiceProperties(year.ToString(), year));

                return ValueTask.FromResult<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(choices);
            }
        }

        public class HourAutocompleteProvider() : IAutocompleteProvider<AutocompleteInteractionContext>
        {
            public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
                ApplicationCommandInteractionDataOption option, 
                AutocompleteInteractionContext context)
            {
                var hours = Enumerable.Range(0, 24)
                    .Select(hour => new DateTime(2000, 1, 1, hour, 0, 0))
                    .Select(dateTime => new ApplicationCommandOptionChoiceProperties(dateTime.ToString("h:00 tt"), dateTime.Hour));

                return ValueTask.FromResult<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(hours);
            }
        }

        private async Task RespondError(string errorMessage)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            {
                Content = errorMessage,
                Flags = MessageFlags.Ephemeral,
            }));
        } 

        [SlashCommand("createremindergroup", "Creates a new reminder group")]
        public async Task CreateReminderGroup(
            [SlashCommandParameter(Name = "name", Description = "The name of the reminder group")] string @name,
            //[SlashCommandParameter(Name = "Description")] string description = "",
            [SlashCommandParameter(Name = "members", Description = "Mention members or roles to add to the group, e.g. @Alice @Bob @Role")] string membersList = ""
            )
        {
            var matches = DiscordMemberIdRegex().Matches(membersList);

            ulong guildId = Context.Guild?.Id
                ?? throw new NullReferenceException("Guild is null");

            bool groupExists = await db.ReminderGroups.AnyAsync(rg => rg.GuildId == guildId && rg.Name == name);
            if (groupExists)
            {
                await RespondError($"❌ Group with the name \"{name}\" already exists");
                return;
            }

            var group = new ReminderGroup
            {
                Name = name,
                GuildId = guildId,
                CreatorId = Context.User.Id,
                Members = [.. matches.Select(m => new ReminderGroupMember
                {
                    MemberId = ulong.Parse(m.Groups["id"].Value),
                    IsRole = m.Groups["role"].Success
                })],
            };

            db.ReminderGroups.Add(group);
            await db.SaveChangesAsync();

            await RespondAsync(InteractionCallback.Message(new() { Embeds = [ new() {
                Title = "Group Created",
                Description = "A new group was created",
                Timestamp = DateTime.UtcNow,
                Color = VGDCColors.Cerulean,
                Fields = 
                [
                    new() 
                    {
                        Name = "Group Name",
                        Value = @name,
                    }, 
                    new() 
                    {
                        Name = "Members",
                        Value = membersList.Replace(' ', '\n'),
                    }
                ],
            }]}));
        }

        [SlashCommand("deleteremindergroup", "Deletes a new reminder group")]
        public async Task DeleteReminderGroup(
            [SlashCommandParameter(Name = "name", Description = "The name of the reminder group")] string @name
            )
        {
            ulong guildId = Context.Guild?.Id
                ?? throw new NullReferenceException("Guild is null");

            ReminderGroup? group = await db.ReminderGroups.FirstOrDefaultAsync(rg => rg.GuildId == guildId && rg.Name == name);
            if (group == null)
            {
                await RespondError($"❌ No group with the name \"{name}\" exists");
                return;
            }

            db.ReminderGroups.Remove(group);
            await db.SaveChangesAsync();

            await RespondAsync(InteractionCallback.Message(new() { Embeds = [ new() {
                Title = "Group Deleted",
                Description = $"Deleted group \"{group.Name}\"",
                Timestamp = DateTime.UtcNow,
                Color = VGDCColors.Cerulean,
            }]}));
        }

        [SlashCommand("listremindergroups", "Lists all reminder groups you created or are a part of")]
        public async Task ListReminderGroups()
        {
            var guildId = Context.Guild!.Id;
            var userId = Context.User.Id;

            var guildUser = Context.User as GuildUser;
            var userRoleIds = guildUser!.RoleIds ?? [];

            var allGroups = await db.ReminderGroups
                .Include(rg => rg.Members)
                .Where(rg => rg.GuildId == guildId && (
                    rg.CreatorId == userId ||
                    rg.Members.Any(m =>
                        (!m.IsRole && m.MemberId == userId) ||
                        (m.IsRole && userRoleIds.Contains(m.MemberId)))))
                .ToListAsync();

            if (allGroups.Count == 0)
            {
                await RespondAsync(InteractionCallback.Message(new()
                {
                    Content = "You are not part of any groups! Create one with /createremindergroup",
                }));
                return;
            }

            var createdGroups = allGroups.Where(rg => rg.CreatorId == userId);
            var memberGroupsWithMention = allGroups.Select(group =>
            {
                var matchingMember = group.Members.FirstOrDefault(m =>
                    (!m.IsRole && m.MemberId == userId) ||
                    (m.IsRole && userRoleIds.Contains(m.MemberId)));

                var mention = matchingMember switch
                {
                    null => "unknown",
                    { IsRole: true } m => $"<@&{m.MemberId}>",
                    { IsRole: false } m => $"<@{m.MemberId}>",
                };

                return $"\"{group.Name}\" from {mention}";
            }).ToList();

            await RespondAsync(InteractionCallback.Message(new() { Embeds = [ new() 
            {
                Title = "My Groups",
                Description = "These are all of your groups",
                Timestamp = DateTime.UtcNow,
                Color = VGDCColors.Cerulean,
                Fields =
                [
                    new()
                    {
                        Name = "Groups Created",
                        Value = createdGroups.Any() ? string.Join('\n', createdGroups.Select(rg => $"\"{rg.Name}\"")) : "None",
                    },
                    new()
                    {
                        Name = "Groups You Are In",
                        Value = memberGroupsWithMention.Count > 0 ? string.Join('\n', memberGroupsWithMention) : "None",
                    }
                ],
            }]}));
        }

        [SlashCommand("addmemberstogroup", "Adds members to a group")]
        public async Task AddMemberToGroup(
            [SlashCommandParameter(Name = "name", Description = "The name of the reminder group")] string @name,
            //[SlashCommandParameter(Name = "Description")] string description = "",
            [SlashCommandParameter(Name = "members", Description = "Mention members or roles to add to the group, e.g. @Alice @Bob @Role")] string membersList = ""
            )
        {
            var matches = DiscordMemberIdRegex().Matches(membersList);

            ulong guildId = Context.Guild?.Id
                ?? throw new NullReferenceException("Guild is null");

            ReminderGroup? group = await db.ReminderGroups.FirstOrDefaultAsync(rg => rg.GuildId == guildId && rg.Name == name);
            if (group == null)
            {
                await RespondError($"❌ No group with the name \"{name}\" exists");
                return;
            }

            var membersToAdd = matches.Select(m => new ReminderGroupMember
            {
                MemberId = ulong.Parse(m.Groups["id"].Value),
                IsRole = m.Groups["role"].Success
            }).ToList();

            group.Members.AddRange(membersToAdd);
            await db.SaveChangesAsync();

            await RespondAsync(InteractionCallback.Message(new() { Embeds = [ new() {
                Title = "Members Added",
                Description = $"Added {membersToAdd.Count} members to \"{group.Name}\"",
                Timestamp = DateTime.UtcNow,
                Color = VGDCColors.Cerulean,
                Fields = 
                [
                    new() 
                    {
                        Name = "New Members",
                        Value = membersList.Replace(' ', '\n'),
                    }
                ],
            }]}));
        }

        [SlashCommand("removemembersfromgroup", "Removes members from a group")]
        public async Task RemoveMembersFromGroup(
            [SlashCommandParameter(Name = "name", Description = "The name of the reminder group")] string @name,
            //[SlashCommandParameter(Name = "Description")] string description = "",
            [SlashCommandParameter(Name = "members", Description = "Mention members or roles to remove from the group, e.g. @Alice @Bob @Role")] string membersList = ""
            )
        {
            var matches = DiscordMemberIdRegex().Matches(membersList);

            ulong guildId = Context.Guild?.Id
                ?? throw new NullReferenceException("Guild is null");

            ReminderGroup? group = await db.ReminderGroups.FirstOrDefaultAsync(rg => rg.GuildId == guildId && rg.Name == name);
            if (group == null)
            {
                await RespondError($"❌ No group with the name \"{name}\" exists");
                return;
            }

            var membersToRemove = matches.Select(m => new ReminderGroupMember
            {
                MemberId = ulong.Parse(m.Groups["id"].Value),
                IsRole = m.Groups["role"].Success
            }).ToList();

            group.Members.RemoveAll(m => membersToRemove.Any(mr => mr.MemberId == m.MemberId && mr.IsRole == m.IsRole));
            await db.SaveChangesAsync();

            await RespondAsync(InteractionCallback.Message(new() { Embeds = [ new() {
                Title = "Members Removed",
                Description = $"Removed {membersToRemove.Count} members from \"{group.Name}\"",
                Timestamp = DateTime.UtcNow,
                Color = VGDCColors.Cerulean,
                Fields = 
                [
                    new() 
                    {
                        Name = "Members Removed",
                        Value = membersList.Replace(' ', '\n'),
                    }
                ],
            }]}));
        }

        public static T GetItemFromJobDataMap<T>(JobDataMap data, string key)
        {
            if (!data.TryGetValue("guildId", out object ?value) || value == null)
                throw new InvalidOperationException("guildId missing from job data");
            return (T)value;
        }

        public class ReminderJob(GatewayClient client, BotDbContext db) : IJob
        {
            private const string defaultMessage = "Reminder!";

            public async Task Execute(IJobExecutionContext context)
            {
                var data = context.JobDetail.JobDataMap;

                var reminder = new ReminderJobData(data);
                var channel = await client.Rest.GetChannelAsync(reminder.ChannelId);

                string mentionsString;
                if (reminder.GroupName != null)
                {
                    var reminderGroup = await db.ReminderGroups
                        .Include(rg => rg.Members)
                        .SingleOrDefaultAsync(rg => rg.GuildId == reminder.GuildId && rg.Name == reminder.GroupName);
                    if (reminderGroup == null) return;

                    var mentionsList = reminderGroup.Members
                        .Select(m => m.IsRole
                            ? $"<@&{m.MemberId}>"
                            : $"<@{m.MemberId}>");
                    mentionsString = string.Join(" ", mentionsList);
                }
                else
                {
                    var userIdStr = reminder.UserIdString
                        ?? throw new ArgumentException("Job execution context has job data without a group or user");

                    mentionsString = $"<@{userIdStr}>";
                }

                string content = $"{mentionsString} {reminder.Message ?? defaultMessage}";

                await ((TextChannel)channel).SendMessageAsync(new MessageProperties { Content = content } );
            }
        }

        [SlashCommand("setreminder", "Sets a new reminder")]
        public async Task SetReminder(
            [SlashCommandParameter(Name = "title", Description = "The title of the reminder")] string title,
            [SlashCommandParameter(Name = "details", Description = "Extra details of the reminder")] string details,
            [SlashCommandParameter(Name = "message", Description = "The message to send in the reminder")] string message,
            [SlashCommandParameter(Name = "day", Description = "The day on which you want to be reminded", MinValue = 1, MaxValue = 31)] int day,
            [SlashCommandParameter(Name = "month", Description = "The month on which you want to be reminded")] Month month,
            [SlashCommandParameter(Name = "year", Description = "The year on which you want to be reminded", AutocompleteProviderType = typeof(YearAutocompleteProvider))] int year,
            [SlashCommandParameter(Name = "hour", Description = "The hour at which you want to be reminded (UTC)", AutocompleteProviderType = typeof(HourAutocompleteProvider))] int hour,
            [SlashCommandParameter(Name = "minute", Description = "The minute at which you want to be reminded", MinValue = 0, MaxValue = 59)] int minute,
            [SlashCommandParameter(Name = "group", Description = "The name of the group to send the reminder to.")] string? group = null,
            [SlashCommandParameter(Name = "frequency", Description = "The name of the group to send the reminder to (default: One Time)")] Frequency frequency = Frequency.OneTime,
            [SlashCommandParameter(Name = "channel", Description = "The channel to send the reminder to")] TextChannel? channel = null
            )
        {
            var guild = Context.Guild;
            var currentChannel = Context.Channel;

            DateTime reminderDateUTC;
            try
            {
                reminderDateUTC = new(year, (int)month, day, hour, minute, 0, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                await RespondError($"❌ Invalid date or time.");
                return;
            }

            if (reminderDateUTC < DateTime.UtcNow)
            {
                await RespondError($"❌ Reminder date and time must be in the future.");
                return;
            }

            IScheduler scheduler = await schedulerFactory.GetScheduler();

            IJobDetail job = JobBuilder.Create<ReminderJob>()
                .WithIdentity(title, "reminders")
                .WithDescription(details)
                .UsingJobData("title", title!)
                .UsingJobData("guildId", (guild?.Id ?? throw new InvalidOperationException("Guild is null")).ToString())
                .UsingJobData("channelId", (channel?.Id ?? currentChannel.Id).ToString())
                .UsingJobData("message", message)
                .UsingJobData("creatorId", Context.User.Id.ToString())
                .StoreDurably()
                .Build();
            if (group != null) job.JobDataMap["group"] = group;
            else job.JobDataMap["userId"] = Context.User.Id.ToString();

            var triggerBuilder = TriggerBuilder.Create()
                .WithIdentity(title!, "reminder-triggers")
                .StartAt(reminderDateUTC);
            if (frequency != Frequency.OneTime)
                triggerBuilder.WithCalendarIntervalSchedule(b => GetScheduleWithInterval(b, frequency)
                    .InTimeZone(TimeZoneInfo.Utc)
                    .PreserveHourOfDayAcrossDaylightSavings(false)
                );
            ITrigger trigger = triggerBuilder.Build();

            await scheduler.ScheduleJob(job, trigger);
        }

        [SlashCommand("listreminders", "Lists all reminders you have")]
        public async Task ListReminders()
        {
            var guildId = Context.Guild!.Id;
            var userId = Context.User.Id;

            var guildUser = Context.User as GuildUser;
            var userRoleIds = guildUser!.RoleIds ?? [];

            IScheduler scheduler = await schedulerFactory.GetScheduler();
            var jobKeys = await scheduler.GetJobKeys(Quartz.Impl.Matchers.GroupMatcher<JobKey>.GroupEquals("reminders"));

            var userReminders = new List<EmbedFieldProperties>();
            foreach (var key in jobKeys)
            {
                var job = await scheduler.GetJobDetail(key);
                if (job == null) continue;
                var reminder = new ReminderJobData(job.JobDataMap);

                var triggers = await scheduler.GetTriggersOfJob(key);
                var nextFireTime = triggers.FirstOrDefault()?.GetNextFireTimeUtc();

                string mentionsString;
                if (reminder.GroupName != null)
                {
                    var reminderGroup = await db.ReminderGroups
                        .Include(rg => rg.Members)
                        .SingleOrDefaultAsync(rg => rg.GuildId == reminder.GuildId && rg.Name == reminder.GroupName);
                    if (reminderGroup == null) return;

                    mentionsString = string.Join(" ", reminderGroup.Members
                        .Select(m => m.IsRole ? $"<@&{m.MemberId}>" : $"<@{m.MemberId}>"));
                }
                else
                {
                    var targetUserId = reminder.UserIdString ?? throw new InvalidOperationException("userId missing");
                    mentionsString = $"<@{targetUserId}>";
                }

                userReminders.Add(new()
                {
                    Name = reminder.Title,
                    Value = $"for {mentionsString} at {nextFireTime?.ToLocalTime().ToString()}",
                });
            }

            await RespondAsync(InteractionCallback.Message(new() { Embeds = [ new() 
            {
                Title = "My Reminders",
                Description = "These are all of the reminders set for you",
                Timestamp = DateTime.UtcNow,
                Color = VGDCColors.Cerulean,
                Fields = userReminders,
            }]}));
        }

        public class ReminderAutocompleteProvider(ISchedulerFactory schedulerFactory)
            : IAutocompleteProvider<AutocompleteInteractionContext>
        {
            public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
                ApplicationCommandInteractionDataOption option, 
                AutocompleteInteractionContext context)
            {
                var scheduler = await schedulerFactory.GetScheduler();

                var jobKeys = await scheduler.GetJobKeys(Quartz.Impl.Matchers.GroupMatcher<JobKey>.GroupEquals("reminders"));
                
                var choices = new List<ApplicationCommandOptionChoiceProperties>();
                foreach (var key in jobKeys)
                {
                    var job = await scheduler.GetJobDetail(key);
                    if (job?.JobDataMap.GetString("creatorId") != context.User.Id.ToString()) continue;

                    choices.Add(new ApplicationCommandOptionChoiceProperties(key.Name, key.Name));
                }

                return choices;
            }
        }

        [SlashCommand("deletereminder", "Deletes a reminder")]
        public async Task DeleteReminder(
            [SlashCommandParameter(Name = "title", Description = "The reminder to delete", 
                AutocompleteProviderType = typeof(ReminderAutocompleteProvider))] string title)
        {
            var scheduler = await schedulerFactory.GetScheduler();
            var deleted = await scheduler.DeleteJob(new JobKey(title, "reminders"));

            if (!deleted)
            {
                await RespondError("❌ Reminder not found.");
                return;
            }

            await RespondAsync(InteractionCallback.Message($"✅ Reminder \"{title}\" deleted."));
        }

        [GeneratedRegex(@"<@(?:(?<role>&)|!?)(?<id>\d+)>")]
        private static partial Regex DiscordMemberIdRegex();
    }

}
