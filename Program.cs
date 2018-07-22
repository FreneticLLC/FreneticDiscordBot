using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord.Net;
using Discord;
using Discord.WebSocket;
using System.Diagnostics;
using FreneticUtilities.FreneticDataSyntax;

public class FreneticDiscordBot
{
    // TODO: Clean and/or rewrite? This static-abusing mess is very lazy. This isn't even in a namespace.

    public Random random = new Random();

    public const string CONFIG_FOLDER = "./config/";

    public const string TOKEN_FILE = CONFIG_FOLDER + "token.txt";

    public const string CONFIG_FILE = CONFIG_FOLDER + "config.fds";

    public static readonly string TOKEN = File.ReadAllText(TOKEN_FILE);

    public const string POSITIVE_PREFIX = "+> ";

    public const string NEGATIVE_PREFIX = "-> ";

    public const string TODO_PREFIX = NEGATIVE_PREFIX + "// TODO: ";

    public static string[] Quotes = File.ReadAllText("./quotes.txt").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n\n", ((char)0x01).ToString()).Split((char)0x01);

    public FDSSection ConfigFile;

    public Object ConfigLock = new Object();

    public DiscordSocketClient client;

    public void Respond(SocketMessage message)
    {
        string[] mesdat = message.Content.Split(' ');
        StringBuilder resBuild = new StringBuilder(message.Content.Length);
        List<string> cmds = new List<string>();
        for (int i = 0; i < mesdat.Length; i++)
        {
            if (mesdat[i].Contains("<") && mesdat[i].Contains(">"))
            {
                continue;
            }
            resBuild.Append(mesdat[i]).Append(" ");
            if (mesdat[i].Length > 0)
            {
                cmds.Add(mesdat[i]);
            }
        }
        if (cmds.Count == 0)
        {
            Console.WriteLine("Empty input, ignoring: " + message.Author.Username);
            return;
        }
        string fullMsg = resBuild.ToString();
        Console.WriteLine("Found input from: (" + message.Author.Username + "), in channel: " + message.Channel.Name + ": " + fullMsg);
        string lowCmd = cmds[0].ToLowerInvariant();
        cmds.RemoveAt(0);
        if (CommonCmds.TryGetValue(lowCmd, out Action<string[], SocketMessage> acto))
        {
            acto.Invoke(cmds.ToArray(), message);
        }
        else
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Unknown command. Consider the __**help**__ command?").Wait();
        }
    }

    public Dictionary<string, Action<string[], SocketMessage>> CommonCmds = new Dictionary<string, Action<string[], SocketMessage>>(1024);

    public class QuoteSeen
    {
        public int QID;

        public DateTime Time;
    }

    public List<QuoteSeen> QuotesSeen = new List<QuoteSeen>();

    public bool QuoteWasSeen(int qid)
    {
        for (int i = 0; i < QuotesSeen.Count; i++)
        {
            if (QuotesSeen[i].QID == qid)
            {
                return true;
            }
        }
        return false;
    }

    void CMD_ShowQuote(string[] cmds, SocketMessage message)
    {
        for (int i = QuotesSeen.Count - 1; i >= 0; i--)
        {
            if (DateTime.UtcNow.Subtract(QuotesSeen[i].Time).TotalMinutes >= 5)
            {
                QuotesSeen.RemoveAt(i);
            }
        }
        int qid = -1;
        if (cmds.Length == 0)
        {
            for (int i = 0; i < 15; i++)
            {
                qid = random.Next(Quotes.Length);
                if (!QuoteWasSeen(qid))
                {
                    break;
                }
            }
        }
        else if (int.TryParse(cmds[0], out qid))
        {
            qid--;
            if (qid < 0)
            {
                qid = 0;
            }
            if (qid >= Quotes.Length)
            {
                qid = Quotes.Length - 1;
            }
        }
        else
        {
            List<int> spots = new List<int>();
            string input_opt = string.Join(" ", cmds);
            for (int i = 0; i < Quotes.Length; i++)
            {
                if (Quotes[i].ToLowerInvariant().Contains(input_opt.ToLowerInvariant()))
                {
                    spots.Add(i);
                }
            }
            if (spots.Count == 0)
            {
                message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Unable to find that quote! Sorry :(").Wait();
                return;
            }
            for (int s = 0; s < 15; s++)
            {
                int temp = random.Next(spots.Count);
                qid = spots[temp];
                if (!QuoteWasSeen(qid))
                {
                    break;
                }
            }
        }
        if (qid >= 0 && qid < Quotes.Length)
        {
            QuotesSeen.Add(new QuoteSeen() { QID = qid, Time = DateTime.UtcNow });
            string quoteRes = POSITIVE_PREFIX + "Quote **" + (qid + 1) + "**:\n```xml\n" + Quotes[qid] + "\n```\n";
            message.Channel.SendMessageAsync(quoteRes).Wait();
        }
    }

    public static string CmdsHelp = 
            "`help`, `quote`, `hello`, `frenetic`, `whois`, "
            + "...";

    public static string CmdsAdminHelp =
            "`restart`, `listeninto`, `redirectnotice`, "
            + "...";

    void CMD_Help(string[] cmds, SocketMessage message)
    {
        if (IsBotCommander(message.Author))
        {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Available Commands:\n" + CmdsHelp
                    + "\nAvailable admin commands: " + CmdsAdminHelp).Wait();
        }
        else
        {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Available Commands:\n" + CmdsHelp).Wait();
        }
    }

    void CMD_Hello(string[] cmds, SocketMessage message)
    {
        message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Hi! I'm a bot! Find my source code at https://github.com/FreneticLLC/FreneticDiscordBot").Wait();
    }

    void CMD_SelfInfo(string[] cmds, SocketMessage message)
    {
        SocketUser user = message.Author;
        foreach (SocketUser tuser in message.MentionedUsers)
        {
            if (tuser.Id != client.CurrentUser.Id)
            {
                user = tuser;
                break;
            }
        }
        EmbedBuilder bed = new EmbedBuilder();
        EmbedAuthorBuilder auth = new EmbedAuthorBuilder();
        auth.Name = user.Username + "#" + user.Discriminator;
        auth.IconUrl = user.GetAvatarUrl();
        auth.Url = user.GetAvatarUrl();
        bed.Author = auth;
        bed.Color = new Color(0xC8, 0x74, 0x4B);
        bed.Title = "Who is " + auth.Name + "?";
        bed.Description = auth.Name + " is a Discord " + (user.IsBot ? "bot" : (user.IsWebhook ? "webhook" : "user")) + "!";
        bed.AddField((efb) => efb.WithName("Discord ID").WithValue(user.Id));
        bed.AddField((efb) => efb.WithName("Discord Join Date").WithValue(FormatDT(user.CreatedAt)));
        bed.AddField((efb) => efb.WithName("Current Status").WithValue(user.Status));
        bed.AddField((efb) => efb.WithName("Current Activity").WithValue(user.Activity == null ? "Nothing." : (user.Activity.Type + ": " + user.Activity.Name)));
        if (user is SocketGuildUser iguser)
        {
            if (iguser.JoinedAt.HasValue)
            {
                bed.AddField(efb => efb.WithName("Joined Here Date").WithValue(FormatDT(iguser.JoinedAt.Value)));
            }
            if (iguser.Nickname != null)
            {
                bed.AddField(efb => efb.WithName("Current Nickname").WithValue(iguser.Nickname));
            }
            string[] roles = iguser.Roles.Where((r) => !r.IsEveryone).Select((r) => r.Name).ToArray();
            bed.AddField((efb) => efb.WithName("Current Roles").WithValue(roles.Length > 0 ? string.Join(", ", roles) : "None currently."));
        }
        bed.Footer = new EmbedFooterBuilder().WithIconUrl(client.CurrentUser.GetAvatarUrl()).WithText("Info provided by FreneticDiscordBot, which is Copyright (C) Frenetic LLC");
        message.Channel.SendMessageAsync(POSITIVE_PREFIX, embed: bed.Build()).Wait();
    }

    bool IsBotCommander(SocketUser usr)
    {
        return (usr as SocketGuildUser).Roles.Where((role) => role.Name.ToLowerInvariant() =="botcommander").FirstOrDefault() != null;
    }

    void CMD_Restart(string[] cmds, SocketMessage message)
    {
        // NOTE: This implies a one-guild bot. A multi-guild bot probably shouldn't have this "BotCommander" role-based verification.
        // But under current scale, a true-admin confirmation isn't worth the bother.
        if (!IsBotCommander(message.Author))
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! That's not for you!").Wait();
            return;
        }
        if (!File.Exists("./start.sh"))
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! That's not valid for my current configuration!").Wait();
        }
        message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Yes, boss. Restarting now...").Wait();
        Process.Start("sh", "./start.sh " + message.Channel.Id);
        Task.Factory.StartNew(() =>
        {
            Console.WriteLine("Shutdown start...");
            for (int i = 0; i < 15; i++)
            {
                Console.WriteLine("T Minus " + (15 - i));
                Task.Delay(1000).Wait();
            }
            Console.WriteLine("Shutdown!");
            Environment.Exit(0);
        });
        client.StopAsync().Wait();
    }

    static string Pad2(int num)
    {
        return num < 10 ? "0" + num : num.ToString();
    }

    static string AddPlus(double d)
    {
        return d < 0 ? d.ToString() : "+" + d;
    }

    static string FormatDT(DateTimeOffset dtoff)
    {
        return dtoff.Year + "/" + Pad2(dtoff.Month) + "/" + Pad2(dtoff.Day)
        + " " + Pad2(dtoff.Hour) + ":" + Pad2(dtoff.Minute) + ":" + Pad2(dtoff.Second)
         + " UTC" + AddPlus(dtoff.Offset.TotalHours);
    }

    void CMD_WhatIsFrenetic(string[] cmds, SocketMessage message)
    {
        EmbedBuilder bed = new EmbedBuilder();
        EmbedAuthorBuilder auth = new EmbedAuthorBuilder();
        auth.Name = "Frenetic LLC";
        auth.IconUrl = client.CurrentUser.GetAvatarUrl();
        auth.Url = "https://freneticllc.com";
        bed.Author = auth;
        bed.Color = new Color(0xC8, 0x74, 0x4B);
        bed.Title = "What is Frenetic LLC?";
        bed.Description = "Frenetic LLC is a California registered limited liability company.";
        bed.AddField((efb) => efb.WithName("What does Frenetic LLC do?").WithValue("In short: We make games!"));
        bed.AddField((efb) => efb.WithName("Who is Frenetic LLC?").WithValue("We are an international team! Check out the #meet-the-team channel on the Frenetic LLC official Discord!"));
        bed.Footer = new EmbedFooterBuilder().WithIconUrl(auth.IconUrl).WithText("Copyright (C) Frenetic LLC");
        message.Channel.SendMessageAsync(POSITIVE_PREFIX, embed: bed.Build()).Wait();
    }

    void CMD_ListenInto(string[] cmds, SocketMessage message)
    {
        if (!IsBotCommander(message.Author))
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! That's not for you!").Wait();
            return;
        }
        ulong serverId = (message.Channel as IGuildChannel).Guild.Id;
        KnownServer ks = ServersConfig.GetOrAdd(serverId, (id) => new KnownServer());
        if (cmds.Length == 0)
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! Consult documentation!").Wait();
            return;
        }
        String goal = cmds[0].ToLowerInvariant();
        IEnumerable<ITextChannel> channels = (message.Channel as IGuildChannel)
                .Guild.GetTextChannelsAsync().Result.Where((tc) => tc.Name.ToLowerInvariant().Replace("#", "").Equals(goal));
        if (channels.Count() == 0)
        {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Disabling sending.").Wait();
            IEnumerable<ITextChannel> channels2 = (message.Channel as IGuildChannel).Guild.GetTextChannelsAsync().Result;
            StringBuilder sbRes = new StringBuilder();
            foreach (ITextChannel itc in channels2)
            {
                sbRes.Append("`").Append(itc.Name).Append("`, ");
            }
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Given: `" + goal + "`, Available: " + sbRes.ToString()).Wait();
            goal = null;
        }
        else
        {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Listening into: " + goal).Wait();
        }
        lock (ConfigLock)
        {
            ks.AllChannelsTo = goal;
            ConfigFile.Set("servers." + serverId + ".all_channels_to", goal);
        }
        SaveChannelConfig();
    }

    void CMD_RedirectNotice(string[] cmds, SocketMessage message)
    {
        if (!IsBotCommander(message.Author))
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! That's not for you!").Wait();
            return;
        }
        ulong serverId = (message.Channel as IGuildChannel).Guild.Id;
        ulong channelId = message.Channel.Id;
        KnownServer ks = ServersConfig.GetOrAdd(serverId, (id) => new KnownServer());
        if (cmds.Length == 0)
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! Consult documentation!").Wait();
            return;
        }
        String goal = cmds[0].ToLowerInvariant();
        IEnumerable<ITextChannel> channels = (message.Channel as IGuildChannel)
                .Guild.GetTextChannelsAsync().Result.Where((tc) => tc.Name.ToLowerInvariant().Replace("#", "").Equals(goal));
        if (channels.Count() == 0)
        {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Disabling redirect notice.").Wait();
            IEnumerable<ITextChannel> channels2 = (message.Channel as IGuildChannel).Guild.GetTextChannelsAsync().Result;
            StringBuilder sbRes = new StringBuilder();
            foreach (ITextChannel itc in channels2)
            {
                sbRes.Append("`").Append(itc.Name).Append("`, ");
            }
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Given: `" + goal + "`, Available: " + sbRes.ToString()).Wait();
            goal = null;
        }
        else
        {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Notifying redirect to: " + goal).Wait();
        }
        lock (ConfigLock)
        {
            if (goal == null)
            {
                ks.ChannelRedirectNotices.Remove(channelId);
            }
            else
            {
                ulong dest = channels.First().Id;
                ks.ChannelRedirectNotices[channelId] = new ChannelRedirectNotice() { RedirectToChannel = dest };
                ConfigFile.Set("servers." + serverId + ".channel_redirect_notices." + channelId, dest);
            }
        }
        SaveChannelConfig();
    }
    
    public void SaveChannelConfig()
    {
        lock (reSaveLock)
        {
            ConfigFile.SaveToFile(CONFIG_FILE);
        }
    }

    public static Object reSaveLock = new Object();

    void DefaultCommands()
    {
        // Various
        CommonCmds["quotes"] = CMD_ShowQuote;
        CommonCmds["quote"] = CMD_ShowQuote;
        CommonCmds["q"] = CMD_ShowQuote;
        CommonCmds["help"] = CMD_Help;
        CommonCmds["halp"] = CMD_Help;
        CommonCmds["helps"] = CMD_Help;
        CommonCmds["halps"] = CMD_Help;
        CommonCmds["hel"] = CMD_Help;
        CommonCmds["hal"] = CMD_Help;
        CommonCmds["h"] = CMD_Help;
        CommonCmds["hello"] = CMD_Hello;
        CommonCmds["hi"] = CMD_Hello;
        CommonCmds["hey"] = CMD_Hello;
        CommonCmds["source"] = CMD_Hello;
        CommonCmds["src"] = CMD_Hello;
        CommonCmds["github"] = CMD_Hello;
        CommonCmds["git"] = CMD_Hello;
        CommonCmds["hub"] = CMD_Hello;
        CommonCmds["who"] = CMD_WhatIsFrenetic;
        CommonCmds["what"] = CMD_WhatIsFrenetic;
        CommonCmds["where"] = CMD_WhatIsFrenetic;
        CommonCmds["why"] = CMD_WhatIsFrenetic;
        CommonCmds["frenetic"] = CMD_WhatIsFrenetic;
        CommonCmds["llc"] = CMD_WhatIsFrenetic;
        CommonCmds["freneticllc"] = CMD_WhatIsFrenetic;
        CommonCmds["website"] = CMD_WhatIsFrenetic;
        CommonCmds["team"] = CMD_WhatIsFrenetic;
        CommonCmds["company"] = CMD_WhatIsFrenetic;
        CommonCmds["business"] = CMD_WhatIsFrenetic;
        CommonCmds["restart"] = CMD_Restart;
        CommonCmds["selfinfo"] = CMD_SelfInfo;
        CommonCmds["whoami"] = CMD_SelfInfo;
        CommonCmds["whois"] = CMD_SelfInfo;
        CommonCmds["userinfo"] = CMD_SelfInfo;
        CommonCmds["userprofile"] = CMD_SelfInfo;
        CommonCmds["profile"] = CMD_SelfInfo;
        CommonCmds["prof"] = CMD_SelfInfo;
        // Admin
        CommonCmds["listeninto"] = CMD_ListenInto;
        CommonCmds["redirectnotice"] = CMD_RedirectNotice;
    }

    public ConcurrentDictionary<ulong, KnownServer> ServersConfig = new ConcurrentDictionary<ulong, KnownServer>();

    public bool ConnectedOnce = false;

    public bool ConnectedCurrently = false;

    public static FreneticDiscordBot CurrentBot = null;

    static void Main(string[] args)
    {
        CurrentBot = new FreneticDiscordBot(args);
    }

    public FreneticDiscordBot(string[] args)
    {
        Console.WriteLine("Preparing...");
        DefaultCommands();
        if (File.Exists(CONFIG_FILE))
        {
            ConfigFile = FDSUtility.ReadFile(CONFIG_FILE);
            FDSSection serversListSection = ConfigFile.GetSection("servers");
            foreach (string serverIdKey in serversListSection.GetRootKeys())
            {
                ulong serverId = ulong.Parse(serverIdKey);
                KnownServer serverObj = new KnownServer();
                ServersConfig[serverId] = serverObj;
                FDSSection serverSection = serversListSection.GetSection(serverIdKey);
                if (serverSection.HasKey("all_channels_to"))
                {
                    string serverAllChannelsTo = serverSection.GetString("all_channels_to");
                    serverObj.AllChannelsTo = serverAllChannelsTo;
                }
                if (serverSection.HasKey("channel_redirect_notices"))
                {
                    FDSSection channelRedirectListSection = serverSection.GetSection("channel_redirect_notices");
                    foreach (string channelRedirectIdKey in channelRedirectListSection.GetRootKeys())
                    {
                        ulong channelSource = ulong.Parse(channelRedirectIdKey);
                        ulong channelTarget = channelRedirectListSection.GetUlong(channelRedirectIdKey).Value;
                        serverObj.ChannelRedirectNotices[channelSource] = new ChannelRedirectNotice() { RedirectToChannel = channelTarget };
                    }
                }
            }
        }
        Console.WriteLine("Loading Discord...");
        DiscordSocketConfig config = new DiscordSocketConfig();
        config.MessageCacheSize = 256;
        client = new DiscordSocketClient(config);
        client.Ready += () =>
        {
            if (StopAllLogic)
            {
                return Task.CompletedTask;
            }
            ConnectedCurrently = true;
            client.SetGameAsync("https://freneticllc.com").Wait();
            if (ConnectedOnce)
            {
                return Task.CompletedTask;
            }
            Console.WriteLine("Args: " + args.Length);
            if (args.Length > 0 && ulong.TryParse(args[0], out ulong a1))
            {
                ISocketMessageChannel chan = client.GetChannel(a1) as ISocketMessageChannel;
                Console.WriteLine("Restarted as per request in channel: " + chan.Name);
                chan.SendMessageAsync(POSITIVE_PREFIX + "Connected and ready!").Wait();
            }
            ConnectedOnce = true;
            return Task.CompletedTask;
        };
        client.MessageReceived += (message) =>
        {
            if (StopAllLogic)
            {
                return Task.CompletedTask;
            }
            if (message.Author.Id == client.CurrentUser.Id)
            {
                return Task.CompletedTask;
            }
            LoopsSilent = 0;
            if (message.Author.IsBot || message.Author.IsWebhook)
            {
                return Task.CompletedTask;
            }
            if (message.Channel.Name.StartsWith("@") || !(message.Channel is SocketGuildChannel sgc))
            {
                Console.WriteLine("Refused message from (" + message.Author.Username + "): (Invalid Channel: " + message.Channel.Name + "): " + message.Content);
                return Task.CompletedTask;
            }
            bool mentionedMe = message.MentionedUsers.Any((su) => su.Id == client.CurrentUser.Id);
            Console.WriteLine("Parsing message from (" + message.Author.Username + "), in channel: " + message.Channel.Name + ": " + message.Content);
            if (ServersConfig.TryGetValue(sgc.Guild.Id, out KnownServer serverSettings))
            {
                if (serverSettings.ChannelRedirectNotices.TryGetValue(message.Channel.Id, out ChannelRedirectNotice notice))
                {
                    if (!notice.UserLastNotices.TryGetValue(message.Author.Id, out DateTimeOffset dto)
                        || DateTimeOffset.UtcNow.Subtract(dto).TotalMinutes > 10.0)
                    {
                        notice.UserLastNotices[message.Author.Id] = DateTimeOffset.UtcNow;
                        Console.WriteLine("Telling user to post in redirect target instead of here.");
                        message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Please post in <#" + notice.RedirectToChannel + "> not here.").Wait();
                    }
                }
            }
            if (mentionedMe)
            {
                try
                {
                    Respond(message);
                }
                catch (Exception ex)
                {
                    if (ex is ThreadAbortException)
                    {
                        throw;
                    }
                    Console.WriteLine("Error handling command: " + ex.ToString());
                }
            }
            else
            {
                String mesLow = message.Content.ToLowerInvariant();
                if (mesLow.StartsWith("yay"))
                {
                    message.Channel.SendMessageAsync(POSITIVE_PREFIX + "YAY!!!").Wait();
                }
            }
            return Task.CompletedTask;
        };
        client.MessageDeleted += (m, c) =>
        {
            if (StopAllLogic)
            {
                return Task.CompletedTask;
            }
            Console.WriteLine("A message was deleted!");
            if (!(c is IGuildChannel channel))
            {
                Console.WriteLine("But it was in a weird channel?");
                return Task.CompletedTask;
            }
            if (!ServersConfig.TryGetValue(channel.Guild.Id, out KnownServer ks))
            {
                Console.WriteLine("But it wasn't in a known guild.");
                return Task.CompletedTask;
            }
            if (ks.AllChannelsTo == null)
            {
                Console.WriteLine("But it wasn't in a listening zone.");
                return Task.CompletedTask;
            }
            IEnumerable<ITextChannel> channels = channel.Guild.GetTextChannelsAsync().Result.Where((tc) => tc.Name.ToLowerInvariant().Replace("#", "").Equals(ks.AllChannelsTo));
            if (channels.Count() == 0)
            {
                Console.WriteLine("Failed to match a channel: " + ks.AllChannelsTo);
                return Task.CompletedTask;
            }
            ITextChannel outputter = channels.First();
            IMessage mValue;
            if (!m.HasValue)
            {
                Console.WriteLine("But I don't see its data... Outputting a blankness note.");
            outputter.SendMessageAsync(POSITIVE_PREFIX + "Message in `"  + c.Name + "` with id `" + c.Id + "` deleted. Specific content not known (likely an old message).").Wait();
                return Task.CompletedTask;
            }
            else
            {
                mValue = m.Value;
            }
            if (mValue.Author.Id == client.CurrentUser.Id)
            {
                Console.WriteLine("Wait, I did that!");
                return Task.CompletedTask;
            }
            if (mValue.Author.IsBot || mValue.Author.IsWebhook)
            {
                Console.WriteLine("But it was bot-posted!");
                return Task.CompletedTask;
            }
            outputter.SendMessageAsync(POSITIVE_PREFIX + "Message deleted (`"  + mValue.Channel.Name + "`)... message from: `"
                    + mValue.Author.Username + "#" + mValue.Author.Discriminator 
                    + "`: ```\n" + mValue.Content.Replace('`', '\'') + "\n```").Wait();
            Console.WriteLine("Outputted!");
            return Task.CompletedTask;
        };
        client.MessageUpdated += (m, mNew, c) =>
        {
            if (StopAllLogic)
            {
                return Task.CompletedTask;
            }
            Console.WriteLine("A message was edited!");
            if (!(c is IGuildChannel channel))
            {
                Console.WriteLine("But it was in a weird channel?");
                return Task.CompletedTask;
            }
            if (!ServersConfig.TryGetValue(channel.Guild.Id, out KnownServer ks))
            {
                Console.WriteLine("But it wasn't in a known guild.");
                return Task.CompletedTask;
            }
            if (ks.AllChannelsTo == null)
            {
                Console.WriteLine("But it wasn't in a listening zone.");
                return Task.CompletedTask;
            }
            IEnumerable<ITextChannel> channels = channel.Guild.GetTextChannelsAsync().Result.Where((tc) => tc.Name.ToLowerInvariant().Replace("#", "").Equals(ks.AllChannelsTo));
            if (channels.Count() == 0)
            {
                Console.WriteLine("Failed to match a channel: " + ks.AllChannelsTo);
                return Task.CompletedTask;
            }
            ITextChannel outputter = channels.First();
            if (mNew.Author.Id == client.CurrentUser.Id)
            {
                Console.WriteLine("Wait, I did that!");
                return Task.CompletedTask;
            }
            if (mNew.Author.IsBot || mNew.Author.IsWebhook)
            {
                Console.WriteLine("But it was bot-posted!");
                return Task.CompletedTask;
            }
            IMessage mValue;
            if (!m.HasValue)
            {
                outputter.SendMessageAsync(POSITIVE_PREFIX + "Message edited(`"  + mNew.Channel.Name + "`)... message from: `"
                        + mNew.Author.Username + "#" + mNew.Author.Discriminator 
                        + "`:\n(Original message unknown)\nBecame:\n```"
                        + mNew.Content.Replace('`', '\'') + "\n```");
                Console.WriteLine("But I don't see its data... outputting what I can!");
                return Task.CompletedTask;
            }
            else
            {
                mValue = m.Value;
            }
            if (mNew.Content == mValue.Content)
            {
                Console.WriteLine("But it was not an edit (reaction or similar instead)!");
                return Task.CompletedTask;
            }
            outputter.SendMessageAsync(POSITIVE_PREFIX + "Message edited(`"  + mValue.Channel.Name + "`)... message from: `"
                    + mValue.Author.Username + "#" + mValue.Author.Discriminator 
                    + "`: ```\n" + mValue.Content.Replace('`', '\'') + "\n```\nBecame:\n```"
                    + mNew.Content.Replace('`', '\'') + "\n```");
            return Task.CompletedTask;
        };
        Console.WriteLine("Prepping monitor...");
        Task.Factory.StartNew(() =>
        {
            while (true)
            {
                Task.Delay(MonitorLoopTime).Wait();
                if (StopAllLogic)
                {
                    return;
                }
                try
                {
                    MonitorLoop();
                }
                catch (Exception ex)
                {
                    if (ex is ThreadAbortException)
                    {
                        throw;
                    }
                    Console.WriteLine("Connection monitor loop had exception: " + ex.ToString());
                }
            }
        });
        Console.WriteLine("Logging in to Discord...");
        client.LoginAsync(TokenType.Bot, TOKEN).Wait();
        Console.WriteLine("Connecting to Discord...");
        client.StartAsync().Wait();
        Console.WriteLine("Running Discord!");
        while (true)
        {
            string read = Console.ReadLine();
            string[] dats = read.Split(new char[] { ' ' }, 2);
            string cmd = dats[0].ToLowerInvariant();
            if (cmd == "quit" || cmd == "stop" || cmd == "exit")
            {
                client.StopAsync().Wait();
                Environment.Exit(0);
            }
        }
    }

    public TimeSpan MonitorLoopTime = new TimeSpan(hours: 0, minutes: 1, seconds: 0);

    public bool MonitorWasFailedAlready = false;

    public bool StopAllLogic = false;

    public void ForceRestartBot()
    {
        lock (MonitorLock)
        {
            StopAllLogic = true;
        }
        Task.Factory.StartNew(() =>
        {
            client.StopAsync().Wait();
        });
        CurrentBot = new FreneticDiscordBot(new string[0]);
    }

    public Object MonitorLock = new Object();

    public long LoopsSilent = 0;

    public long LoopsTotal = 0;

    public void MonitorLoop()
    {
        bool isConnected;
        lock (MonitorLock)
        {
            LoopsSilent++;
            LoopsTotal++;
            isConnected = ConnectedCurrently && client.ConnectionState == ConnectionState.Connected;
        }
        if (!isConnected)
        {
            Console.WriteLine("Monitor detected disconnected state!");
        }
        if (LoopsSilent > 60)
        {
            Console.WriteLine("Monitor detected over an hour of silence, and is assuming a disconnected state!");
            isConnected = false;
        }
        if (LoopsTotal > 60 * 12)
        {
            Console.WriteLine("Monitor detected that the bot has been running for over 12 hours, and will restart soon!");
            isConnected = false;
        }
        if (isConnected)
        {
            MonitorWasFailedAlready = false;
        }
        else
        {
            if (MonitorWasFailedAlready)
            {
                Console.WriteLine("Monitor is enforcing a restart!");
                ForceRestartBot();
            }
            MonitorWasFailedAlready = true;
        }
    }

    public class ChannelRedirectNotice
    {
        public ulong RedirectToChannel;

        public ConcurrentDictionary<ulong, DateTimeOffset> UserLastNotices = new ConcurrentDictionary<ulong, DateTimeOffset>();
    }

    public class KnownServer
    {
        public string AllChannelsTo = null;

        public Dictionary<ulong, ChannelRedirectNotice> ChannelRedirectNotices = new Dictionary<ulong, ChannelRedirectNotice>();
    }
}
