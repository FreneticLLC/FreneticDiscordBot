using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord.Net;
using Discord;
using Discord.WebSocket;
using System.Diagnostics;

public partial class Program
{
    public static Random random = new Random();

    public static DiscordSocketClient client;

    public static readonly string TOKEN = File.ReadAllText("./conf.txt");

    public const string POSITIVE_PREFIX = "+> ";

    public const string NEGATIVE_PREFIX = "-> ";

    public const string TODO_PREFIX = NEGATIVE_PREFIX + "// TODO: ";

    public static string[] Quotes = File.ReadAllText("./quotes.txt").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n\n", ((char)0x01).ToString()).Split((char)0x01);

    public static void Respond(SocketMessage message)
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
            cmds.Add(mesdat[i]);
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

    public static Dictionary<string, Action<string[], SocketMessage>> CommonCmds = new Dictionary<string, Action<string[], SocketMessage>>(1024);

    public class QuoteSeen
    {
        public int QID;
        public DateTime Time;
    }

    public static List<QuoteSeen> QuotesSeen = new List<QuoteSeen>();

    public static bool QuoteWasSeen(int qid)
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

    static void CMD_ShowQuote(string[] cmds, SocketMessage message)
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
            for (int i = 0; i < Quotes.Length; i++)
            {
                for (int x = 0; x < cmds.Length; x++)
                {
                    if (Quotes[i].ToLowerInvariant().Contains(cmds[x].ToLowerInvariant()))
                    {
                        spots.Add(i);
                    }
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
            string quoteRes = POSITIVE_PREFIX + "Quote **" + qid + "**:\n```xml\n" + Quotes[qid] + "\n```\n";
            message.Channel.SendMessageAsync(quoteRes).Wait();
        }
    }

    public static string CmdsHelp = 
            "`help`, `quote`, `hello`, "
            + "...";

    static void CMD_Help(string[] cmds, SocketMessage message)
    {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Available Commands:\n" + CmdsHelp).Wait();
    }

    static void CMD_Hello(string[] cmds, SocketMessage message)
    {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Hi! I'm a bot! Find my source code at https://github.com/FreneticLLC/FreneticDiscordBot").Wait();
    }

    static bool IsBotCommander(SocketUser usr)
    {
        return (usr as SocketGuildUser).Roles.Where((role) => role.Name.ToLowerInvariant() =="botcommander").FirstOrDefault() != null;
    }

    static void CMD_Restart(string[] cmds, SocketMessage message)
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

    static void CMD_WhatIsFrenetic(string[] cmds, SocketMessage message)
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
        bed.AddField((efb) => efb.WithName("Who is Frenetic LLC?").WithValue("We are an international team! Check out the #meet-the-team channel!"));
        bed.Footer = new EmbedFooterBuilder().WithIconUrl(auth.IconUrl).WithText("Copyright (C) Frenetic LLC");
            message.Channel.SendMessageAsync(POSITIVE_PREFIX, embed: bed.Build()).Wait();
    }

    static void DefaultCommands()
    {
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
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Preparing...");
        DefaultCommands();
        Console.WriteLine("Loading Discord...");
        client = new DiscordSocketClient();
        client.Ready += () =>
        {
            Console.WriteLine("Args: " + args.Length);
            if (args.Length > 0 && ulong.TryParse(args[0], out ulong a1))
            {
                ISocketMessageChannel chan = (client.GetChannel(a1) as ISocketMessageChannel);
                Console.WriteLine("Restarted as per request in channel: " + chan.Name);
                chan.SendMessageAsync(POSITIVE_PREFIX + "Connected and ready!").Wait();
            }
            return Task.CompletedTask;
        };
        client.MessageReceived += (message) =>
        {
            if (message.Author.Id == client.CurrentUser.Id)
            {
                return Task.CompletedTask;
            }
            if (message.Channel.Name.StartsWith("@") || !(message.Channel is SocketGuildChannel))
            {
                Console.WriteLine("Refused message from (" + message.Author.Username + "): (Invalid Channel: " + message.Channel.Name + "): " + message.Content);
                return Task.CompletedTask;
            }
            bool mentionedMe = false;
            foreach (SocketUser user in message.MentionedUsers)
            {
                if (user.Id == client.CurrentUser.Id)
                {
                    mentionedMe = true;
                    break;
                }
            }
                Console.WriteLine("Parsing message from (" + message.Author.Username + "), in channel: " + message.Channel.Name + ": " + message.Content);
            if (mentionedMe)
            {
                Respond(message);
            }
            return Task.CompletedTask;
        };
        Console.WriteLine("Logging in to Discord...");
        client.LoginAsync(TokenType.Bot, TOKEN).Wait();
        Console.WriteLine("Connecting to Discord...");
        client.StartAsync().Wait();
        Console.WriteLine("Running Discord!");
        Task.Delay(-1).Wait(); // Politely wait FOREVER (or until program shutdown, of course!)
    }
}
