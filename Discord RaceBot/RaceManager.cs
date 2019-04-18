using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Collections.Generic;
using Discord.WebSocket;
using Discord;

namespace Discord_RaceBot
{
    public static class RaceManager
    {
        //Save the Discord client object so we can access Discord from this class
        public static DiscordSocketClient client { get; set; }

        public static async Task UpdateRacesChannelAsync()
        {

            //get the lists of races
            List<DatabaseHandler.RaceItem> entryOpenRaces = DatabaseHandler.GetRaceList("Entry Open");
            List<DatabaseHandler.RaceItem> startingRaces = DatabaseHandler.GetRaceList("Countdown");
            List<DatabaseHandler.RaceItem> inProgressRaces = DatabaseHandler.GetRaceList("In Progress");
            List<DatabaseHandler.RaceItem> completedRaces = DatabaseHandler.GetRaceList("Recently Completed");

            //get the races channel so we can send messages
            SocketTextChannel racesChannel = (SocketTextChannel)client.GetChannel(Globals.RacesChannelId);

            //for building the message
            string message;

            //First, delete the old messages in the races channel
            var oldMessages = await racesChannel.GetMessagesAsync().FlattenAsync();
            await racesChannel.DeleteMessagesAsync(oldMessages);

            //display the races lists as three separate messages.
            message = "**Races that are open to enter:**\n";
            if (entryOpenRaces.Count > 0)
            {
                foreach (var item in entryOpenRaces)
                {
                    message += "<#" + item.TextChannelId + ">: " + item.Description + "\n";
                }                
            }
            else message += "No races found\n";

            message += "\n**Races starting soon:**\n";
            if (startingRaces.Count > 0)
            {                
                foreach (var item in startingRaces)
                {
                    message += "<#" + item.TextChannelId + ">: " + item.Description + "\n";
                }               
            }
            else message += "No races found\n";

            message += "\n**Races in progress:\n**";
            if (inProgressRaces.Count > 0)
            {                
                foreach (var item in inProgressRaces)
                {
                    message += "<#" + item.TextChannelId + ">: " + item.Description + "\n";
                }  
            }
            else message += "No races found\n";

            message += "\n**Recently completed races:\n**";
            if (completedRaces.Count > 0)
            {
                foreach (var item in completedRaces)
                {
                    message += "<#" + item.TextChannelId + ">: " + item.Description + "\n";
                }
            }
            else message += "No races found";
            await racesChannel.SendMessageAsync(message);
        }

        public static async Task NewRaceAsync(string description, ulong owner)
        {
            //Create the database record and get the new Race Id.
            ulong newRaceId = DatabaseHandler.NewRace(description, owner);
                        
            var guild = client.GetGuild(Globals.GuildId);

            //Create a role for the race.
            var newRaceRole = await guild.CreateRoleAsync("race-" + newRaceId);

            //allow this role to be mentionable
            await newRaceRole.ModifyAsync(x =>
            {
                x.Mentionable = true;
            });

            //Create a new text channel in the server
            var newTextChannel = await guild.CreateTextChannelAsync("race-" + newRaceId,
                x =>
                {
                    x.Topic = "**Entry Open** | " + description + " | Entered: 0 | Ready: 0";
                    x.CategoryId = Globals.RacesCategoryId;
                });

            //Create a voice channel as well.
            var newVoiceChannel = await guild.CreateVoiceChannelAsync("race-" + newRaceId,
                x =>
                {
                    x.CategoryId = Globals.RacesCategoryId;
                });
                        
            //Update the race in the database with the new channels/role
            DatabaseHandler.UpdateRace(
                newRaceId,
                TextChannelId: newTextChannel.Id,
                VoiceChannelId: newVoiceChannel.Id,
                RoleId: newRaceRole.Id);

            //reply to the command on #racebot
            var racebotChannel = guild.GetTextChannel(Globals.RacebotChannelId);
            await racebotChannel.SendMessageAsync("New race channel " + newTextChannel.Mention + " created for " + description + ". GLHF!");
            _ = UpdateRacesChannelAsync();

        }

        public static async Task DeleteRaceAsync(DatabaseHandler.RaceItem Race, string Status)
        {
            //If the status is Aborted, set the race status. 

            if (Status == "Aborted")
                DatabaseHandler.UpdateRace(Race.RaceId, Status: "Aborted");
            else if (Status == "Complete")
                DatabaseHandler.UpdateRace(Race.RaceId, Status: "Complete");
            else if (Status != null)
                return;

            //Get the channels and role from Discord
            var guild = client.GetGuild(Globals.GuildId);
            var textChannel = guild.GetChannel(Race.TextChannelId);
            var voiceChannel = guild.GetChannel(Race.VoiceChannelId);
            var raceRole = guild.GetRole(Race.RoleId);

            //Delete the channels and role from the Discord server
            await textChannel.DeleteAsync();
            await voiceChannel.DeleteAsync();
            await raceRole.DeleteAsync();
        }

        public static async Task AddEntrantAsync(DatabaseHandler.RaceItem Race, ulong UserId)
        {
            var raceServer = client.GetGuild(Globals.GuildId);
            var entrant = raceServer.GetUser(UserId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            //attempt to join the race
            bool result = DatabaseHandler.JoinRace(Race.RaceId, UserId);
            //if the command returns true, then the user is probably already joined
            if (result == true) await raceChannel.SendMessageAsync(entrant.Mention + ", I couldn't enter you (did you already enter?)");

            else
            {
                var raceRole = raceServer.GetRole(Race.RoleId);

                //assign the correct race role to the user
                await entrant.AddRoleAsync(raceRole);
                await raceChannel.SendMessageAsync(entrant.Mention + ", you are entered in the race. Type '.ready' when you are ready to start.");
            }
            _ = UpdateChannelTopicAsync(Race.RaceId);
        }

        public static async Task SetEntrantStatusAsync(DatabaseHandler.RaceItem Race, ulong UserId, string Status)
        {
            var raceServer = client.GetGuild(Globals.GuildId);
            var entrant = raceServer.GetUser(UserId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);
                       
            //Attempt to update the database. If the update function returns true, then the user isn't entered in the race
            if (!DatabaseHandler.UpdateEntry(Race.RaceId, UserId, Status))
            {
                await raceChannel.SendMessageAsync(entrant.Username + " is **" + Status + "**");
                await AttemptRaceStartAsync(Race);
            }
            else await raceChannel.SendMessageAsync(entrant.Mention + ", I couldn't change your status (are you entered?)");

            
        }

        public static async Task MarkEntrantDoneAsync(DatabaseHandler.RaceItem Race, ulong UserId)
        {

            var raceServer = client.GetGuild(Globals.GuildId);
            var entrant = raceServer.GetUser(UserId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            //Attempt to update the database. If the update function returns null, then the user isn't entered in the race
            DatabaseHandler.EntrantItem entrantInformation = DatabaseHandler.MarkEntrantFinished(Race.RaceId, UserId, Race.StartTime);
                        
            if (entrantInformation != null)
            {
                var raceRole = raceServer.GetRole(Race.RoleId);
                await entrant.RemoveRoleAsync(raceRole);                
                await raceChannel.SendMessageAsync(entrant.Mention + ", you finished in **"+AddOrdinal(entrantInformation.Place) +"** place with a time of **" + entrantInformation.FinishedTime + "**");
                await AttemptRaceFinishAsync(Race);
            }
            else await raceChannel.SendMessageAsync(entrant.Mention + ", I couldn't mark you as Done (are you entered?)");

        }

        public static async Task RemoveEntrantAsync(DatabaseHandler.RaceItem Race, ulong UserId)
        {
            var guild = client.GetGuild(Globals.GuildId);
            var raceChannel = guild.GetTextChannel(Race.TextChannelId);
            var raceRole = guild.GetRole(Race.RoleId);
            var entrant = guild.GetUser(UserId);

            if (!DatabaseHandler.DeleteEntrant(Race.RaceId, UserId))
            {
                await entrant.RemoveRoleAsync(raceRole);
                await raceChannel.SendMessageAsync(entrant.Mention + ", you have been removed from the race.");
                await AttemptRaceStartAsync(Race);
            }
            else await raceChannel.SendMessageAsync(entrant.Mention + ", I couldn't remove you from the race (are you entered?)");
        }

        public static async Task ForfeitEntrantAsync(DatabaseHandler.RaceItem Race, ulong UserId)
        {
            var guild = client.GetGuild(Globals.GuildId);
            var raceChannel = guild.GetTextChannel(Race.TextChannelId);
            var raceRole = guild.GetRole(Race.RoleId);
            var entrant = guild.GetUser(UserId);

            DatabaseHandler.EntrantItem entrantStatus = DatabaseHandler.GetEntrantInformation(Race.RaceId, UserId);
            //if no result was returned, the user isn't entered
            if(entrantStatus == null)
            {
                await raceChannel.SendMessageAsync(entrant.Mention + ", I couldn't forfeit you from the race (are you entered?)");
                return;
            }

            //We can't forfeit a player who isn't still racing.
            if(entrantStatus.Status != "Ready")
            {
                await raceChannel.SendMessageAsync(entrant.Mention + ", you can't use that command right now.");
                return;
            }
            //attempt to forfeit the racer
            if(!DatabaseHandler.UpdateEntry(Race.RaceId, UserId, "Forfeited"))
            {
                await entrant.RemoveRoleAsync(raceRole);
                await raceChannel.SendMessageAsync(entrant.Mention + ", you have forfeited from the race.");
                await AttemptRaceFinishAsync(Race);
            }
        }

        public static async Task ShowTimeAsync(DatabaseHandler.RaceItem Race)
        {
            var raceServer = client.GetGuild(Globals.GuildId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            //calculate how much time has passed since the race start time and now
            TimeSpan elapsedTime = Race.StartTime - DateTime.Now;

            //Reply with elapsed time
            await raceChannel.SendMessageAsync("Elapsed time: **" + elapsedTime.ToString(@"hh\:mm\:ss") + "**");
        }
        
        public static async Task AttemptRaceStartAsync(DatabaseHandler.RaceItem Race)
        {
            DatabaseHandler.EntrantsSummary entrantsSummary = DatabaseHandler.GetEntrantsSummary(Race.RaceId);

            var raceServer = client.GetGuild(Globals.GuildId);
            var raceRole = raceServer.GetRole(Race.RoleId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            if (entrantsSummary.Ready == entrantsSummary.TotalEntrants)
            {
                if (entrantsSummary.TotalEntrants > 1)
                {
                    //All of the entrants are ready, and we have enough entrants, so we can start the race
                    await raceChannel.SendMessageAsync(raceRole.Mention + " Everyone is ready! Race will start in 10 seconds.");
                    DatabaseHandler.UpdateRace(Race.RaceId, Status: "Countdown");
                    var newTimer = new CountdownTimer();
                    newTimer.Interval = 7000;
                    newTimer.race = Race;
                    newTimer.AutoReset = false;
                    newTimer.Elapsed += CountdownRaceAsync;
                    newTimer.Enabled = true;
                    newTimer.Start();
                    _ = UpdateRacesChannelAsync();
                }
            }
            _ = UpdateChannelTopicAsync(Race.RaceId);
        }

        public static async Task AttemptRaceFinishAsync(DatabaseHandler.RaceItem Race)
        {
            DatabaseHandler.EntrantsSummary entrantsSummary = DatabaseHandler.GetEntrantsSummary(Race.RaceId);

            var raceServer = client.GetGuild(Globals.GuildId);
            var raceRole = raceServer.GetRole(Race.RoleId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            //Racers are stored as "Ready" until they finish, forfeit, or are disqualified
            if (entrantsSummary.Ready == 0)
            {
                await raceChannel.SendMessageAsync("Everyone is finished! GGs all around! This channel will be deleted in 10 minutes.");
                DatabaseHandler.UpdateRace(Race.RaceId, Status: "Recently Completed");
                var newTimer = new CountdownTimer();
                newTimer.Interval = 600000;
                newTimer.race = Race;
                newTimer.AutoReset = false;
                newTimer.Elapsed += DeleteFinishedRaceAsync;
                newTimer.Enabled = true;
                newTimer.Start();
                _ = UpdateRacesChannelAsync();

            }
            _ = UpdateChannelTopicAsync(Race.RaceId);
        }

        public static async Task UpdateChannelTopicAsync(ulong RaceId)
        {
            DatabaseHandler.RaceItem race = DatabaseHandler.GetRaceInformation(RaceId);
            DatabaseHandler.EntrantsSummary entrantsSummary = DatabaseHandler.GetEntrantsSummary(race.RaceId);

            SocketTextChannel raceChannel = (SocketTextChannel)client.GetChannel(race.TextChannelId);
            string newTopic = "**" + race.Status + "** | " + race.Description;

            if(race.Status=="Entry Open")
                newTopic += " | Entered: " + (entrantsSummary.NotReady + entrantsSummary.Ready) + " | Ready: " + entrantsSummary.Ready;    
            
            else
                newTopic += " | Racing: " + (entrantsSummary.Ready + entrantsSummary.Done + entrantsSummary.Forfeited + entrantsSummary.Disqalified) + " | Done: " + entrantsSummary.Done + " | Forfeited: " + entrantsSummary.Forfeited;

            await raceChannel.ModifyAsync(x =>
            {
                x.Topic = newTopic;
            });
        }

        private static async void CountdownRaceAsync(Object source, ElapsedEventArgs e)
        {
            DatabaseHandler.RaceItem race = ((CountdownTimer)source).race;
            var raceChannel = (SocketTextChannel)client.GetChannel(race.TextChannelId);
                        
            await raceChannel.SendMessageAsync("**3**");
            Thread.Sleep(1000);
            await raceChannel.SendMessageAsync("**2**");
            Thread.Sleep(1000);
            await raceChannel.SendMessageAsync("**1**");
            Thread.Sleep(1000);
            await raceChannel.SendMessageAsync("**GO!**");

            string startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            DatabaseHandler.UpdateRace(race.RaceId, Status: "In Progress", StartTime: startTime);
            await UpdateRacesChannelAsync();
            await UpdateChannelTopicAsync(race.RaceId);
        }

        private static async void DeleteFinishedRaceAsync(Object source, ElapsedEventArgs e)
        {
            DatabaseHandler.RaceItem race = ((CountdownTimer)source).race;
            await DeleteRaceAsync(race, "Complete");
            await UpdateRacesChannelAsync();
        }

        private static string AddOrdinal(int num)
        {
            if (num <= 0) return num.ToString();

            switch (num % 100)
            {
                case 11:
                case 12:
                case 13:
                    return num + "th";
            }

            switch (num % 10)
            {
                case 1:
                    return num + "st";
                case 2:
                    return num + "nd";
                case 3:
                    return num + "rd";
                default:
                    return num + "th";
            }
        }
    }
}
