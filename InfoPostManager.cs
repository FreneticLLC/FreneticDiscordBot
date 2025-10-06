using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticExtensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreneticDiscordBot;

public class InfoPostManager
{
    public class InfoPost
    {
        public ulong GuildID;

        public ulong ChannelID;

        public string[] Messages;
    }

    public string GitFolder;

    public DiscordSocketClient Client;

    public FreneticDiscordBot Bot;

    public long LastPullTime = 0;

    public List<InfoPost> Posts;

    public void Init(FDSSection section, DiscordSocketClient _client, FreneticDiscordBot _bot)
    {
        Client = _client;
        Bot = _bot;
        GitFolder = section.GetString("git_folder").Replace('\\', '/').TrimEnd('/') ?? throw new Exception("Missing 'git_folder' in InfoPostManager section!");
        if (!Directory.Exists(GitFolder))
        {
            throw new Exception("Git folder in config does not exist on data drive!");
        }
        Client.Ready += () =>
        {
            InitFromGitFolder();
            return Task.CompletedTask;
        };
        Bot.IdleTick += Loop;
    }

    public void Loop()
    {
        if (Bot.StopAllLogic)
        {
            return;
        }
        if (Posts is null)
        {
            return;
        }
        if (Environment.TickCount64 - LastPullTime < 15 * 60 * 1000)
        {
            return;
        }
        RunCheck();
    }

    public void RunCheck()
    {
        try
        {
            LastPullTime = Environment.TickCount64;
            string head = RunGitProcess("rev-parse HEAD", GitFolder);
            RunGitProcess("pull", GitFolder);
            string newHead = RunGitProcess("rev-parse HEAD", GitFolder);
            if (head == newHead)
            {
                Console.WriteLine($"[InfoPostManager] Did git pull, no change (head is {head})");
                return;
            }
            Console.WriteLine($"[InfoPostManager] Did git pull, head was {head}, now is {newHead}, will process...");
            InitFromGitFolder();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InfoPostManager] Error in RunCheck: {ex}");
        }
    }

    public static string RunGitProcess(string args, string folder)
    {
        folder = Path.GetFullPath(folder);
        Process p = Process.Start(new ProcessStartInfo("git", args) { WorkingDirectory = folder, RedirectStandardOutput = true, UseShellExecute = false });
        p.WaitForExit();
        return p.StandardOutput.ReadToEnd().Trim();
    }

    public void InitFromGitFolder()
    {
        Console.WriteLine("[InfoPostManager] Doing init...");
        try
        {
            Posts ??= [];
            bool anyNew = false;
            foreach (string folder in Directory.EnumerateDirectories(GitFolder).Select(f => f.Replace('\\', '/').AfterLast('/')))
            {
                if (!ulong.TryParse(folder, out ulong guildId))
                {
                    continue;
                }
                SocketGuild guild = Client.GetGuild(guildId);
                if (guild is null)
                {
                    Console.WriteLine($"[InfoPostManager] Guild not found: {guildId}");
                    continue;
                }
                foreach (string file in Directory.EnumerateFiles($"{GitFolder}/{folder}", "*.md").Select(f => f.Replace('\\', '/').AfterLast('/')))
                {
                    string channel = file.Before('.');
                    if (!ulong.TryParse(channel, out ulong channelId))
                    {
                        Console.WriteLine($"[InfoPostManager] Invalid channel ID in file name: '{file}'");
                        continue;
                    }
                    SocketTextChannel chan = guild.GetTextChannel(channelId);
                    if (chan is null)
                    {
                        Console.WriteLine($"[InfoPostManager] Channel not found: {guildId}/{channelId}");
                        continue;
                    }
                    string messageText = File.ReadAllText($"{GitFolder}/{folder}/{file}");
                    string[] messages = messageText.Split("<BREAK>", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (messages.Length == 0)
                    {
                        Console.WriteLine($"[InfoPostManager] No messages found in file: {file}");
                        continue;
                    }
                    if (messages.Any(m => m.Length >= 2000))
                    {
                        Console.WriteLine($"[InfoPostManager] Message too long in file: {file} - lengths are {messages.Select(m => m.Length).JoinString(", ")}");
                        continue;
                    }
                    if (messages.Length > 25)
                    {
                        Console.WriteLine($"[InfoPostManager] Too many messages in file: {file}, has {messages.Length} messages");
                        continue;
                    }
                    InfoPost post = Posts.FirstOrDefault(p => p.GuildID == guildId && p.ChannelID == channelId);
                    if (post is null)
                    {
                        Posts.Add(new InfoPost() { GuildID = guildId, ChannelID = channelId, Messages = messages });
                        anyNew = true;
                        continue;
                    }
                    if (post.Messages.SequenceEqual(messages))
                    {
                        continue;
                    }
                    post.Messages = messages;
                    Console.WriteLine($"[InfoPostManager] Post for {post.GuildID}/{post.ChannelID} ({chan.Name}) was modified, reposting...");
                    SendPostNow(chan, post).Wait();
                    Console.WriteLine($"[InfoPostManager] Updated post for {guildId}/{channelId}");
                }
            }
            if (Posts.IsEmpty())
            {
                Console.WriteLine("[InfoPostManager] No posts found at all! Nothing being maintained.");
            }
            if (anyNew)
            {
                LoadCurrentPosts().Wait();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InfoPostManager] Exception in InitFromGitFolder: {ex}");
        }
    }

    public SocketTextChannel ChannelFor(InfoPost post)
    {
        SocketGuild guild = Client.GetGuild(post.GuildID);
        if (guild is null)
        {
            Console.WriteLine($"[InfoPostManager] Guild not found: {post.GuildID}");
            return null;
        }
        SocketTextChannel chan = guild.GetTextChannel(post.ChannelID);
        if (chan is null)
        {
            Console.WriteLine($"[InfoPostManager] Channel not found: {post.GuildID}/{post.ChannelID}");
            return null;
        }
        return chan;
    }

    public static async Task<List<IMessage>> MessagesFor(SocketTextChannel channel)
    {
        return [.. (await channel.GetMessagesAsync(50).FlattenAsync())];
    }

    public async Task SendPostNow(SocketTextChannel chan, InfoPost post)
    {
        List<IMessage> existing = await MessagesFor(chan);
        foreach (string msg in post.Messages)
        {
            await chan.SendMessageAsync(msg, allowedMentions: AllowedMentions.None);
        }
        foreach (IMessage message in existing)
        {
            if (message.Author.Id == Client.CurrentUser.Id)
            {
                await message.DeleteAsync();
            }
        }
    }

    public async Task LoadCurrentPosts()
    {
        foreach (InfoPost post in Posts)
        {
            SocketTextChannel chan = ChannelFor(post);
            if (chan is null)
            {
                continue;
            }
            List<string> existingMessages = [];
            foreach (IMessage message in await MessagesFor(chan))
            {
                if (message.Author.Id == Client.CurrentUser.Id)
                {
                    existingMessages.Add(message.Content);
                }
            }
            if (existingMessages.SequenceEqual(post.Messages) || existingMessages.AsEnumerable().Reverse().SequenceEqual(post.Messages))
            {
                continue;
            }
            Console.WriteLine($"[InfoPostManager] Post for {post.GuildID}/{post.ChannelID} ({chan.Name}) is out of date in LoadCurrentPosts, reposting...");
            await SendPostNow(chan, post);
        }
    }
}
