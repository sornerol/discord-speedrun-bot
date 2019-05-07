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
        public static DiscordSocketClient Client { get; set; }

        //We may need to stop some timers from firing. We'll store these timers in a list
        private static List<CountdownTimer> _forceStartTimerList = new List<CountdownTimer>();
        private static List<CountdownTimer> _completedRaceTimerList = new List<CountdownTimer>();


        public static async Task UpdateRacesChannelAsync()
        {
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            //get the lists of races
            List<RaceItem> entryOpenRaces = database.GetRaceList("Entry Open");
            List<RaceItem> startingRaces = database.GetRaceList("Countdown");
            List<RaceItem> inProgressRaces = database.GetRaceList("In Progress");
            List<RaceItem> completedRaces = database.GetRaceList("Recently Completed");

            database.Dispose();

            //get the races channel so we can send messages
            SocketTextChannel racesChannel = (SocketTextChannel)Client.GetChannel(Globals.RacesChannelId);

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
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            ulong newRaceId = database.NewRace(description, owner);
            
            //Get the server from Discord
            var guild = Client.GetGuild(Globals.GuildId);

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
            database.UpdateRace(
                newRaceId,
                TextChannelId: newTextChannel.Id,
                VoiceChannelId: newVoiceChannel.Id,
                RoleId: newRaceRole.Id);

            database.Dispose();

            //reply to the command on #racebot
            var racebotChannel = guild.GetTextChannel(Globals.RacebotChannelId);
            await racebotChannel.SendMessageAsync("New race channel " + newTextChannel.Mention + " created for " + description + ". GLHF!");
            _ = UpdateRacesChannelAsync();

        }

        public static async Task DeleteRaceAsync(RaceItem Race, string Status)
        {
            //Don't use this command if the status is anything other than "Aborted" or "Complete"
            if (Status != "Aborted" && Status != "Complete") return;

            //Update the database with the appropriate status
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            database.UpdateRace(Race.RaceId, Status: Status);
            database.Dispose();

            //Get the channels and role from Discord
            var guild = Client.GetGuild(Globals.GuildId);
            var textChannel = guild.GetChannel(Race.TextChannelId);
            var voiceChannel = guild.GetChannel(Race.VoiceChannelId);
            var raceRole = guild.GetRole(Race.RoleId);

            //Delete the channels and role from the Discord server
            await textChannel.DeleteAsync();
            await voiceChannel.DeleteAsync();
            await raceRole.DeleteAsync();

            //check for and remove any force start timers that may be waiting to fire
            RemoveTimer(_forceStartTimerList, Race.RaceId);
        }

        public static async Task AddEntrantAsync(RaceItem Race, ulong UserId)
        {
            //get the required information from Discord
            var raceServer = Client.GetGuild(Globals.GuildId);
            var entrant = raceServer.GetUser(UserId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);

            //attempt to join the race. if the command returns true, then the user is probably already joined
            if (!database.JoinRace(Race.RaceId, UserId))
            {
                //get the race role from Discord
                var raceRole = raceServer.GetRole(Race.RoleId);

                //assign the correct race role to the user
                await entrant.AddRoleAsync(raceRole);
                await raceChannel.SendMessageAsync(entrant.Mention + ", you are entered in the race. Type '.ready' when you are ready to start.");
                
                //Update the race channel topic to reflect the correct number of people joined.
                _ = UpdateChannelTopicAsync(Race.RaceId);
            }
            database.Dispose();
        }

        public static async Task SetEntrantStatusAsync(RaceItem Race, ulong UserId, string Status)
        {
            var raceServer = Client.GetGuild(Globals.GuildId);
            var entrant = raceServer.GetUser(UserId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);

            //Attempt to update the database. If the update function returns true, then the user isn't entered in the race
            if (!database.UpdateEntry(Race.RaceId, UserId, Status))
            {
                await raceChannel.SendMessageAsync(entrant.Username + " is **" + Status + "**");
                await AttemptRaceStartAsync(Race);
            }
            database.Dispose();            
        }

        public static async Task MarkEntrantDoneAsync(RaceItem Race, ulong UserId)
        {
            //Get the required information from Discord
            var raceServer = Client.GetGuild(Globals.GuildId);
            var entrant = raceServer.GetUser(UserId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            //Attempt to update the database. If the update function returns null, then the user isn't entered in the race
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            EntrantItem entrantInformation = database.MarkEntrantFinished(Race.RaceId, UserId, Race.StartTime);
            database.Dispose();

            //if we get a result back from MarkEntrantFinished, let the racer know their place and finish time
            if (entrantInformation == null) return;
            
             var raceRole = raceServer.GetRole(Race.RoleId);
             await entrant.RemoveRoleAsync(raceRole);                
             await raceChannel.SendMessageAsync(entrant.Mention + ", you finished in **"+AddOrdinal(entrantInformation.Place) +"** place with a time of **" + entrantInformation.FinishedTime + "**");
             await AttemptRaceFinishAsync(Race);
        }

        public static async Task MarkEntrantNotDoneAsync(RaceItem Race, ulong UserId)
        {
            //Get the required information from Discord
            var raceServer = Client.GetGuild(Globals.GuildId);
            var entrant = raceServer.GetUser(UserId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);
            

            //Attempt to update the database. If the update function returns null, then the user isn't entered in the race
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            if (database.MarkEntrantNotFinished(Race.RaceId, UserId)) return;
            var raceRole = raceServer.GetRole(Race.RoleId);
            await entrant.AddRoleAsync(raceRole);

            await raceChannel.SendMessageAsync(entrant.Mention + ", I marked you as not done. Keep racing!");

            if(Race.Status == "Recently Completed")
            {
                database.UpdateRace(Race.RaceId, Status: "In Progress");
                RemoveTimer(_completedRaceTimerList, Race.RaceId);
                _ = UpdateRacesChannelAsync();
            }
            _ = UpdateChannelTopicAsync(Race.RaceId);
            database.Dispose();
        }

        public static async Task RemoveEntrantAsync(RaceItem Race, ulong UserId)
        {
            //get required info from Discord
            var guild = Client.GetGuild(Globals.GuildId);
            var raceChannel = guild.GetTextChannel(Race.TextChannelId);
            var raceRole = guild.GetRole(Race.RoleId);
            var entrant = guild.GetUser(UserId);

            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);

            //attempt to delete the entrant from the race. If DeleteEntrant returns true, they aren't entered in the race
            if (!database.DeleteEntrant(Race.RaceId, UserId))
            {
                await entrant.RemoveRoleAsync(raceRole);
                await raceChannel.SendMessageAsync(entrant.Mention + ", you have been removed from the race.");
                await AttemptRaceStartAsync(Race);
            }
            database.Dispose(); 
        }

        public static async Task ForfeitEntrantAsync(RaceItem Race, ulong UserId)
        {
            //get required info from Discord
            var guild = Client.GetGuild(Globals.GuildId);
            var raceChannel = guild.GetTextChannel(Race.TextChannelId);
            var raceRole = guild.GetRole(Race.RoleId);
            var entrant = guild.GetUser(UserId);

            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);

            //Get the get the entrant's status.
            EntrantItem entrantStatus = database.GetEntrantInformation(Race.RaceId, UserId);

            //if no result was returned, the user isn't entered
            if(entrantStatus == null)
            {
                database.Dispose();
                return;
            }

            //We can't forfeit a player who isn't still racing.
            if(entrantStatus.Status != "Ready")
            {
                database.Dispose();
                return;
            }

            //attempt to forfeit the racer
            if(!database.UpdateEntry(Race.RaceId, UserId, "Forfeited"))
            {
                await entrant.RemoveRoleAsync(raceRole);
                await raceChannel.SendMessageAsync(entrant.Mention + ", you have forfeited from the race.");
                await AttemptRaceFinishAsync(Race);
            }
            //UpdateEntry shouldn't return true since we've already checked to see if the racer is entered, but if it does, we need to let the racer know.
            else await raceChannel.SendMessageAsync(entrant.Mention + ", something went wrong when I tried to remove you. Please let a moderator know.");

            database.Dispose();
        }

        public static async Task ShowTimeAsync(RaceItem Race)
        {
            var raceServer = Client.GetGuild(Globals.GuildId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            //calculate how much time has passed since the race start time and now
            TimeSpan elapsedTime = Race.StartTime - DateTime.Now;

            //Reply with elapsed time
            await raceChannel.SendMessageAsync("Elapsed time: **" + elapsedTime.ToString(@"hh\:mm\:ss") + "**");
        }
        
        public static async Task<bool> AttemptRaceStartAsync(RaceItem Race)
        {
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);

            EntrantsSummary entrantsSummary = database.GetEntrantsSummary(Race.RaceId);

            var raceServer = Client.GetGuild(Globals.GuildId);
            var raceRole = raceServer.GetRole(Race.RoleId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            //sometimes we need to know if we're actually starting the race.
            bool raceIsStarting = false;

            //See if the number of ready entrants + disqualified entrants equals the total number of entrants
            //It is possible (but rare) for an entrant to be marked disqualified before a race starts
            //Excessive DQs may result in a penalty at some point, so it's important to record them
            if (entrantsSummary.Ready + entrantsSummary.Disqalified == entrantsSummary.TotalEntrants)
            {
                //we don't want a situation where there is only one racer who is ready, but the race starts
                //because of DQed entrants.
                if (entrantsSummary.Ready > 1)
                {
                    //All of the entrants are ready, and we have enough entrants, so we can start the race
                    await raceChannel.SendMessageAsync(raceRole.Mention + " Everyone is ready! Race will start in 10 seconds.");
                    database.UpdateRace(Race.RaceId, Status: "Countdown");
                    var newTimer = new CountdownTimer();
                    newTimer.Interval = 7000;
                    newTimer.race = Race;
                    newTimer.AutoReset = false;
                    newTimer.Elapsed += CountdownRaceAsync;
                    newTimer.Enabled = true;
                    newTimer.Start();

                    //check for and remove any force start timers that may be waiting to fire
                    RemoveTimer(_forceStartTimerList, Race.RaceId);
                    _ = UpdateRacesChannelAsync();
                    raceIsStarting = true;
                }
            }

            _ = UpdateChannelTopicAsync(Race.RaceId);
            database.Dispose();
            return raceIsStarting;
        }

        public static async Task<bool> AttemptRaceFinishAsync(RaceItem Race)
        {
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);

            EntrantsSummary entrantsSummary = database.GetEntrantsSummary(Race.RaceId);

            var raceServer = Client.GetGuild(Globals.GuildId);
            var raceRole = raceServer.GetRole(Race.RoleId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            //we sometimes may need to know if the race is actually finishing
            bool raceIsFinishing = false;

            //Racers are stored as "Ready" until they finish, forfeit, or are disqualified
            if (entrantsSummary.Ready == 0)
            {
                await raceChannel.SendMessageAsync("Everyone is finished! GGs all around! This channel will be deleted in 10 minutes.");
                database.UpdateRace(Race.RaceId, Status: "Recently Completed");
                var newTimer = new CountdownTimer();
                newTimer.Interval = 600000;
                newTimer.race = Race;
                newTimer.AutoReset = false;
                newTimer.Elapsed += DeleteFinishedRaceAsync;
                newTimer.Enabled = true;
                newTimer.Start();
                _completedRaceTimerList.Add(newTimer);
                raceIsFinishing = true;
                _ = UpdateRacesChannelAsync();

            }

            _ = UpdateChannelTopicAsync(Race.RaceId);
            database.Dispose();
            return raceIsFinishing;
        }

        public static async Task BeginForceStartAsync(RaceItem Race)
        {
            var raceServer = Client.GetGuild(Globals.GuildId);
            var raceRole = raceServer.GetRole(Race.RoleId);
            var raceChannel = raceServer.GetTextChannel(Race.TextChannelId);

            //Set the race status to "Countdown" so no new entrants can join
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            database.UpdateRace(Race.RaceId, Status: "Countdown");
            database.Dispose();

            //We're going to set a timer to give the remaining entrants time to ready up
            await raceChannel.SendMessageAsync(raceRole.Mention + ", a moderator is force starting this race. Entrants who are not ready within 30 seconds will be kicked from the race.");
            var newTimer = new CountdownTimer();
            newTimer.Interval = 30000;
            newTimer.race = Race;
            newTimer.AutoReset = false;
            newTimer.Elapsed += ForceStartRaceAsync;
            newTimer.Enabled = true;
            newTimer.Start();
            _forceStartTimerList.Add(newTimer);

            _ = UpdateChannelTopicAsync(Race.RaceId);
            _ = UpdateRacesChannelAsync();
        }

        public static async Task UpdateChannelTopicAsync(ulong RaceId)
        {
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);

            RaceItem race = database.GetRaceInformation(RaceId);
            EntrantsSummary entrantsSummary = database.GetEntrantsSummary(race.RaceId);
            database.Dispose();

            SocketTextChannel raceChannel = (SocketTextChannel)Client.GetChannel(race.TextChannelId);
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
            RaceItem race = ((CountdownTimer)source).race;
            var raceChannel = (SocketTextChannel)Client.GetChannel(race.TextChannelId);
                        
            await raceChannel.SendMessageAsync("**3**");
            Thread.Sleep(1000);
            await raceChannel.SendMessageAsync("**2**");
            Thread.Sleep(1000);
            await raceChannel.SendMessageAsync("**1**");
            Thread.Sleep(1000);
            await raceChannel.SendMessageAsync("**GO!**");

            string startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            database.UpdateRace(race.RaceId, Status: "In Progress", StartTime: startTime);
            database.Dispose();
            await UpdateRacesChannelAsync();
            await UpdateChannelTopicAsync(race.RaceId);
        }

        private static async void DeleteFinishedRaceAsync(Object source, ElapsedEventArgs e)
        {
            RaceItem race = ((CountdownTimer)source).race;
            await DeleteRaceAsync(race, "Complete");
            await UpdateRacesChannelAsync();
        }

        private static async void ForceStartRaceAsync(Object source, ElapsedEventArgs e)
        {
            RaceItem race = ((CountdownTimer)source).race;
            var raceChannel = (SocketTextChannel)Client.GetChannel(race.TextChannelId);
            var guild = Client.GetGuild(Globals.GuildId);
            var raceRole = guild.GetRole(race.RoleId);

            //get the list of players who are not ready
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            List<EntrantItem> playersToRemove = database.GetEntrantList(race.RaceId, "Not Ready");

            int kickedPlayers = 0;

            foreach(EntrantItem entrant in playersToRemove)
            {
                var discordEntrant = guild.GetUser(entrant.UserId);
                if (!database.DeleteEntrant(race.RaceId, entrant.UserId))
                {
                    kickedPlayers++;
                    await discordEntrant.RemoveRoleAsync(raceRole);
                    await discordEntrant.SendMessageAsync("You were kicked from **Race " + race.RaceId + ": " + race.Description + "** because you did not make yourself ready in a timely manner.");
                }
            }

            //Attempt to force start the race. If the race doesn't start, let everyone know and set
            //the race status back to "Entry Open"
            bool raceIsStarting = await AttemptRaceStartAsync(race);
                        
            if (!raceIsStarting)
            {
                await raceChannel.SendMessageAsync("I could not force start this race because there aren't enough participants who are ready.");
                database.UpdateRace(race.RaceId, Status: "Entry Open");
                await UpdateChannelTopicAsync(race.RaceId);

            }

            database.Dispose();

            //remove the timer from our list of force start timers
            _forceStartTimerList.Remove((CountdownTimer)source);

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

        private static void RemoveTimer(List<CountdownTimer> timerList, ulong RaceId)
        {
            //When we do something that would interfere with a race that is about to force start, we need to remove the
            //force start timer to prevent it from firing.
            foreach (CountdownTimer timer in timerList)
            {
                if (timer.race.RaceId == RaceId)
                {
                    timer.Stop();
                    timer.Dispose();
                    timerList.Remove(timer);
                    break;
                }
            }
        }
    }
}
