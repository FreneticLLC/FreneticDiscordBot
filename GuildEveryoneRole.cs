using Discord.WebSocket;
using FreneticUtilities.FreneticDataSyntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreneticDiscordBot;

public class GuildEveryoneRole
{
    public Dictionary<ulong, ulong> GuildToEveryoneRole = [];

    public void Init(FDSSection section, DiscordSocketClient client)
    {
        foreach (string key in section.GetRootKeys())
        {
            GuildToEveryoneRole[ulong.Parse(key)] = section.GetUlong(key).Value;
        }
        client.UserJoined += async (user) =>
        {
            if (GuildToEveryoneRole.TryGetValue(user.Guild.Id, out ulong everyoneRole))
            {
                Console.WriteLine($"[Guild Everyone Role] User joined, adding everyone role: {user.Id}");
                await user.AddRoleAsync(everyoneRole);
            }
        };
        client.Ready += async () =>
        {
            try
            {
                foreach ((ulong guild, ulong role) in GuildToEveryoneRole)
                {
                    SocketGuild g = client.GetGuild(guild);
                    await g.DownloadUsersAsync();
                    Console.WriteLine($"[Guild Everyone Role] checking {g.Users.Count} users...");
                    foreach (SocketGuildUser user in g.Users)
                    {
                        if (!user.Roles.Any(r => r.Id == role))
                        {
                            Console.WriteLine($"[Guild Everyone Role] adding role to user: {user.Id}");
                            await user.AddRoleAsync(g.GetRole(role));
                        }
                    }
                    Console.WriteLine($"[Guild Everyone Role] complete.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Role bouncer Ready error: {ex}");
            }
        };
    }
}
