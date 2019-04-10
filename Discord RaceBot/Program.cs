using System;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace Discord_RaceBot
{
    class Program
    {
        private DiscordSocketClient _client;
        private CommandService _commandService;
        private CommandHandler _commandHandler;

        static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            //hook up our unhandled exception handling code
            Thread.GetDomain().UnhandledException += new UnhandledExceptionEventHandler(Application_UnhandledException);

            Globals.LoadGlobalsFromConfigFile();
            
            _client = new DiscordSocketClient();
            _commandService = new CommandService();

            _client.Log += Log; //hook our Log function
            
            //connect to Discord
            await _client.LoginAsync(TokenType.Bot, Globals.Token);
            await _client.StartAsync();

            //Set up the command handler
            _commandHandler = new CommandHandler(_client, _commandService);
            await _commandHandler.InstallCommandsAsync();
            
            //Let RaceManager have access to the DiscordSocketClient
            RaceManager.client = _client;
            
            //block this task until the program is closed
            await Task.Delay(-1);
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        //For unhandled exceptions, write the information to a log file
        public static void Application_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = (Exception) e.ExceptionObject;
            string exceptionString = DateTime.Now.ToString() +
                ": " + exception.GetType() +
                ": " + exception.Message + "\n" +
                ": " + exception.StackTrace + "\n\n"; 
            File.AppendAllText("exception.log", exceptionString);
        }
    }
}
