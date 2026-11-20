using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace JohnVGDCBot;

public class KeyTrackerOptions
{
    public int TotalKeys { get; set; } = 4;
}

[SlashCommand("key", "Track storage room keys", 
    DefaultGuildPermissions = (Permissions)0,
    Contexts = [InteractionContextType.Guild])]
public partial class KeyTrackerModule(BotDbContext db, IOptions<KeyTrackerOptions> options) : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly int _totalKeys = options.Value.TotalKeys;

    [SubSlashCommand("list", "List who currently has a key")]
    public async Task List()
    {
        var guildId = Context.Interaction.GuildId!.Value;
        var currentHoldings = await db.KeyHoldings
            .Where(h => h.GuildId == guildId && h.ReleasedAt == null)
            .ToListAsync();

        if (currentHoldings.Count == 0)
        {
            await RespondAsync(InteractionCallback.Message(new()
            {
                Content = $"Nobody has a key, so all {_totalKeys} are available!"
            }));
            return;
        }

        string currentHoldingsText = string.Join("\n", currentHoldings
            .Select(h => $"**<@{h.HolderID}>**"));     
        await RespondAsync(InteractionCallback.Message(new() { Embeds = [ new() 
        {
            Title = "🔑 Key Havers",
            Description = currentHoldingsText,
            Timestamp = DateTime.UtcNow,
            Color = VGDCColors.Cerulean,
        }]}));
    }

    [SubSlashCommand("give", "Give your key to someone else")]
    public async Task Give(
        [SlashCommandParameter(Description = "Who you're giving your key to")] User receiver)
    {
        var guildId = Context.Interaction.GuildId!.Value;
        var userId = Context.User.Id;
        var now = DateTime.UtcNow;

        if (receiver.IsBot)
        {
            await RespondError("❌ You can't give a key to a bot!");
            return;
        } 
        if (receiver.Id == userId)
        {
            await RespondError("❌ You can't give a key to yourself!");
            return;
        }
        if (receiver is not GuildUser)
        {
            await RespondError("❌ That person isn't in this server.");
            return;
        }

        bool receiverHasKey = await db.KeyHoldings.AnyAsync(h =>
            h.GuildId == guildId &&
            h.HolderID == receiver.Id &&
            h.ReleasedAt == null);
        if (receiverHasKey)
        {
            await RespondError($"❌ <@{receiver.Id}> already has a key!");
            return;
        }

        var holding = await db.KeyHoldings.FirstOrDefaultAsync(h =>
            h.GuildId == guildId &&
            h.HolderID == userId &&
            h.ReleasedAt == null); 

        if (holding is null)
        {
            await RespondError ("❌ You don't have a key!");
            return;
        }

        holding.ReleasedAt = now;
        db.KeyHoldings.Add(new()
        {
            GuildId = guildId,
            HolderID = receiver.Id,
            AcquiredAt = now,
            RecordedById = userId,
        });

        try
        {
            await db.SaveChangesAsync();
        } 
        catch (DbUpdateConcurrencyException)
        {
            await RespondError("❌ That key was already given. Try again maybe.");
            return;
        }
        catch (DbUpdateException)
        {
            await RespondError("❌ Someone just beat you to it. Try Again.");
            return;
        }

        await RespondAsync(InteractionCallback.Message(new()
        {
            Content = $"<@{userId}> gave their key to <@{receiver.Id}>.",
            AllowedMentions = AllowedMentionsProperties.None,
        }));
        return;
    }

    [SubSlashCommand("return", "Return your key")]
    public async Task Return()
    {
        var guildId = Context.Interaction.GuildId!.Value;
        var userId = Context.User.Id;
        var now = DateTime.UtcNow;

        var holding = await db.KeyHoldings.FirstOrDefaultAsync(h =>
            h.GuildId == guildId &&
            h.HolderID == userId &&
            h.ReleasedAt == null); 

        if (holding is null)
        {
            await RespondError("❌ You don't have a key!");
            return;
        }

        holding.ReleasedAt = now;

        try
        {
            await db.SaveChangesAsync();
        } 
        catch (DbUpdateConcurrencyException)
        {
            await RespondError("❌ That key was already returned. Try again maybe.");
            return;
        }

        await RespondAsync(InteractionCallback.Message(new()
        {
            Content = $"<@{userId}> returned their key.",
            AllowedMentions = AllowedMentionsProperties.None,
        }));
        return;
    }

    [SubSlashCommand("take", "Take a key if there's one available that nobody has")]
    public async Task Take()
    {
        var guildId = Context.Interaction.GuildId!.Value;
        var userId = Context.User.Id; 
        var now = DateTime.UtcNow;

        bool hasKey = await db.KeyHoldings.AnyAsync(h =>
            h.GuildId == guildId &&
            h.HolderID == userId &&
            h.ReleasedAt == null);
        if (hasKey)
        {
            await RespondError("❌ You already have a key!");
            return;
        }

        int keysHeld = await db.KeyHoldings.CountAsync(h => 
            h.GuildId == guildId &&
            h.ReleasedAt == null);
        int availableKeys = _totalKeys - keysHeld;
        if (availableKeys <= 0)
        {
            await RespondError("❌ There are no available keys. Run `/key list` to see who has a key.");
            return;
        }

        db.KeyHoldings.Add(new()
        {
            GuildId = guildId,
            HolderID = userId,
            AcquiredAt = now,
            RecordedById = userId,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            await RespondError("❌ Someone just beat you to it. Try Again.");
            return;
        }

        await RespondAsync(InteractionCallback.Message(new()
        {
            Content = $"<@{userId}> took a key.",
            AllowedMentions = AllowedMentionsProperties.None,
        }));
        return;
    }

    private async Task RespondError(string errorMessage, bool shouldPing = false)
    {
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
        {
            Content = errorMessage,
            Flags = MessageFlags.Ephemeral,
            AllowedMentions = shouldPing ? AllowedMentionsProperties.All : AllowedMentionsProperties.None,
        }));
    } 
}
