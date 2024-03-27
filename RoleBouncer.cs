using Discord.WebSocket;
using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreneticDiscordBot;

public class RoleBouncer
{
    public ulong MainServerID, CopyServerID;

    public Dictionary<ulong, ulong> RoleMap = [];

    public DiscordSocketClient Client;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public void Init(FDSSection section, DiscordSocketClient client)
    {
        Client = client;
        MainServerID = section.GetUlong("main").Value;
        CopyServerID = section.GetUlong("copy").Value;
        FDSSection mapSection = section.GetSection("roles");
        foreach (string key in mapSection.GetRootKeys())
        {
            RoleMap[ulong.Parse(key)] = mapSection.GetUlong(key).Value;
        }
        client.UserUpdated += async (old_user, new_user) =>
        {
            try
            {
                if (old_user is not SocketGuildUser old_guild_user || new_user is not SocketGuildUser new_guild_user || old_guild_user.Guild.Id != MainServerID)
                {
                    return;
                }
                if (old_guild_user.Roles.Select(r => r.Id).JoinString(",") == new_guild_user.Roles.Select(r => r.Id).JoinString(","))
                {
                    return;
                }
                _ = Task.Run(async () => await RecheckUser(new_guild_user));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Role bouncer UserUpdated error: {ex}");
            }
        };
        client.Ready += async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                SocketGuild mainGuild = client.GetGuild(MainServerID);
                await mainGuild.DownloadUsersAsync();
                foreach (SocketGuildUser user in mainGuild.Users)
                {
                    await RecheckUser(user);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Role bouncer Ready error: {ex}");
            }
        };
    }

    public async Task RecheckUser(SocketUser user)
    {
        try
        {
            SocketGuildUser mainUser = Client.GetGuild(MainServerID).GetUser(user.Id);
            SocketGuild copyGuild = Client.GetGuild(CopyServerID);
            SocketGuildUser copyUser = copyGuild.GetUser(user.Id);
            if (mainUser is null || copyUser is null)
            {
                return;
            }
            ulong[] mainRoleIds = [.. mainUser.Roles.Select(r => r.Id).Order()];
            string roles = mainRoleIds.JoinString(",");
            List<ulong> newRoles = mainRoleIds.Except(RoleMap.Values).ToList();
            foreach ((ulong mainId, ulong copyId) in RoleMap)
            {
                if (mainRoleIds.Contains(mainId))
                {
                    newRoles.Add(copyId);
                }
            }
            ulong[] newRoleIds = [.. newRoles.Order()];
            string newRolesStr = newRoleIds.JoinString(",");
            if (roles == newRolesStr)
            {
                return;
            }
            await copyUser.ModifyAsync((props) =>
            {
                props.Roles = newRoleIds.Select(id => copyGuild.GetRole(id)).ToList();
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Role bouncer Recheck error: {ex}");
        }
    }
}
