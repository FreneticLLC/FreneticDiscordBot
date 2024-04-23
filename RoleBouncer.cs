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
        client.GuildMemberUpdated += async (old_user, new_user) =>
        {
            try
            {
                Console.WriteLine($"[RoleBouncer Debug] User updated: {new_user.Id}");
                if (!old_user.HasValue || old_user.Value.Guild.Id != MainServerID)
                {
                    Console.WriteLine($"[RoleBouncer Debug] User updated not in main server or not cached: {new_user.Id}");
                    return;
                }
                if (old_user.Value.Roles.Select(r => r.Id).JoinString(",") == new_user.Roles.Select(r => r.Id).JoinString(","))
                {
                    Console.WriteLine($"[RoleBouncer Debug] User updated roles match: {new_user.Id}");
                    return;
                }
                _ = Task.Run(async () => await RecheckUser(new_user));
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
                SocketGuild copyGuild = Client.GetGuild(CopyServerID);
                await copyGuild.DownloadUsersAsync();
                Console.WriteLine($"[RoleBouncer Debug] Ready, checking {mainGuild.Users.Count} users...");
                foreach (SocketGuildUser user in mainGuild.Users)
                {
                    await RecheckUser(user);
                }
                Console.WriteLine($"[RoleBouncer Debug] Ready complete.");
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
                Console.WriteLine($"[RoleBouncer Debug] User not found in one or both guilds: {user.Id}");
                return;
            }
            ulong[] mainRoleIds = [.. mainUser.Roles.Select(r => r.Id).Order()];
            ulong[] copyRoleIds = [.. copyUser.Roles.Select(r => r.Id).Order()];
            string origCopyRoles = copyRoleIds.JoinString(",");
            HashSet<ulong> newRoles = copyRoleIds.Except(RoleMap.Values).ToHashSet();
            foreach ((ulong mainId, ulong copyId) in RoleMap)
            {
                if (mainRoleIds.Contains(mainId))
                {
                    newRoles.Add(copyId);
                }
            }
            ulong[] newRoleIds = [.. newRoles.Order()];
            string newRolesStr = newRoleIds.JoinString(",");
            if (origCopyRoles == newRolesStr)
            {
                Console.WriteLine($"[RoleBouncer Debug] User roles match: {user.Id}");
                return;
            }
            Console.WriteLine($"[RoleBouncer Debug] User roles do not match, updating: {user.Id}");
            await copyUser.ModifyAsync((props) =>
            {
                props.Roles = newRoleIds.Select(id => copyGuild.GetRole(id)).ToList();
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Role bouncer Recheck error while updating user {user.Id}: {ex}");
        }
    }
}
