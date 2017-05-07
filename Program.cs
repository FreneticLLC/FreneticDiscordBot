using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord.Net;
using Discord;
using Discord.WebSocket;

public partial class Program
{
    public static Random random = new Random();

    public static DiscordSocketClient client;

    public static readonly string TOKEN = File.ReadAllText("./conf.txt");

    static void Main(string[] args)
    {
        Console.WriteLine("Loading Discord...");
        client = new DiscordSocketClient();
        client.MessageReceived += (message) =>
        {
            if (message.Author.Id == client.CurrentUser.Id)
            {
                return Task.CompletedTask;
            }
            // TODO!
            return Task.CompletedTask;
        };
        Console.WriteLine("Logging in to Discord...");
        client.LoginAsync(TokenType.Bot, TOKEN).Wait();
        Console.WriteLine("Connecting to Discord...");
        client.ConnectAsync().Wait();
        Console.WriteLine("Running Discord!");
        Task.Delay(-1).Wait(); // Politely wait FOREVER (or until program shutdown, of course!)
    }
}
