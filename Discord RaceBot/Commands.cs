using System.Threading.Tasks;
using System;
using System.Linq;
using System.Collections.Generic;
using Discord;
using Discord.Commands;
using Discord.WebSocket;


namespace Discord_RaceBot
{
    public class CommandsModule : ModuleBase<SocketCommandContext>
    {
        [Command("version")]
        [Summary("Displays Racebot's version.")]
        public async Task CommandVersion()
        {
            //RaceBot should only handle this command if it comes from #racebot
            if (!(Context.Channel.Id == Globals.RacebotChannelId)) return;

            await ReplyAsync(Globals.Version);            

            return;
        }
        [Command("startrace")]
        [Summary("Opens a new race channel in the Discord server")]
        public Task CommandStartRace([Remainder][Summary("Description for the race channel")] string description)
        {
            //RaceBot should only handle this command if it comes from #racebot
            if (!(Context.Channel.Id == Globals.RacebotChannelId)) return Task.CompletedTask;

            string cleanDescription = CleanDescription(description);                       

            return Task.Factory.StartNew(()=>RaceManager.NewRaceAsync(cleanDescription, Context.User.Id));
        }
        
        [Command("join")]
        [Summary("Adds the user to the race. Must be used within a race channel")]
        public Task CommandJoinRace()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //get the RaceId by removing "race-" from the channel name we're in
            ulong RaceId = GetRaceId(Context.Channel.Name);

            //get the race information from the database
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            //Verify that the race is still open to entry          
            if (race.Status != "Entry Open") return Task.CompletedTask;      

            return Task.Factory.StartNew(() => RaceManager.AddEntrantAsync(race, Context.User.Id));
        }
        
        [Command("ready")]
        [Summary("Sets a racer's status to 'ready'")]
        public Task CommandReady()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //This command is only available when the race is open for entry, so we need to get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            if (race.Status != "Entry Open" && race.Status != "Countdown") return Task.CompletedTask;                    

            return Task.Factory.StartNew(() => RaceManager.SetEntrantStatusAsync(race, Context.User.Id, "Ready"));
        }

        [Command("notready")]
        [Summary("Sets a racer's status to 'notready'")]
        public Task CommandNotReady()
        {

            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //This command is only available when the race is open for entry, so we need to get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            if (race.Status != "Entry Open") return Task.CompletedTask;
            
            return Task.Factory.StartNew(() => RaceManager.SetEntrantStatusAsync(race, Context.User.Id, "Not Ready"));
        }

        [Command("done")]
        [Summary("Used when a racer has completed the race goal")]
        public Task CommandDone()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //we need to get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            if (race.Status != "In Progress") return Task.CompletedTask;

            return Task.Factory.StartNew(() => RaceManager.MarkEntrantDoneAsync(race, Context.User.Id));
        }

        [Command("notdone")]
        [Summary("Used when a racer accidentally uses the .done command")]
        public Task CommandNotDone()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //we need to get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            EntrantItem entrant = database.GetEntrantInformation(RaceId, Context.User.Id);
            database.Dispose();

            //don't continue with this command if the entrant isn't marked done.
            if (entrant.Status != "Done") return Task.CompletedTask;

            if (race.Status != "In Progress" && race.Status != "Recently Completed") return Task.CompletedTask;

            return Task.Factory.StartNew(() => RaceManager.MarkEntrantNotDoneAsync(race, Context.User.Id));
        }

        [Command("quit")]
        [Summary("Removes the user from the race. If the race has started, it will be recorded as a forfeit.")]
        public Task CommandQuit()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //we need to get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            //depending on the race status, choose the correct way to handle the withdrawal (either remove outright or mark as forfeited.
            if (race.Status == "Entry Open" || race.Status == "Countdown") return Task.Factory.StartNew(() => RaceManager.RemoveEntrantAsync(race, Context.User.Id));
            else if (race.Status == "In Progress") return Task.Factory.StartNew(() => RaceManager.ForfeitEntrantAsync(race, Context.User.Id));

            return Task.CompletedTask;
        }

        [Command("time")]
        [Summary("Displays how much time has elapsed since the race began.")]
        public Task CommandTime()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //we need the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            //Verify that the race is still open to entry          
            if (race.Status != "In Progress") return Task.CompletedTask;

            return Task.Factory.StartNew(() => RaceManager.ShowTimeAsync(race));
        }

        [Command("comment")]
        [Summary("Records a comment for the entrant.")]
        public async Task CommandCommentAsync([Remainder][Summary("The comment to leave for this entrant")] string comment)
        {

            await ReplyAsync("This command is not implemented yet.");
        }

        [Command("cancel")]
        [Summary("Cancels the race and deletes the channels/role")]
        public Task CommandCancel()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            ulong RaceId = GetRaceId(Context.Channel.Name);
            //get the race information from the database
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            //we need to check to see if the user has permission to cancel this race
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList();
            bool userHasPermission = false;

            //check to see if the user is a moderator first.
            foreach (SocketRole item in userRoles)
            {
                if (item.Name.ToLower() == "moderator")
                {
                    userHasPermission = true;
                    break;
                }
            }

            //if the user is not a moderator and they are the owner of the race, they can still cancel it if it's open for entry.
            if (!userHasPermission && race.Owner == Context.User.Id)
            {
                if (race.Status == "Entry Open") userHasPermission = true;
            }

            //If the user isn't allowed to use this command, return
            if (!userHasPermission) return Task.CompletedTask;

            //users can only cancel "Entry Open" or "In Progress" races
            if (race.Status == "Entry Open" || race.Status == "In Progress")
            {
                return Task.Factory.StartNew(
                    () =>
                    {
                        _ = RaceManager.DeleteRaceAsync(race, "Aborted");
                        _ = RaceManager.UpdateRacesChannelAsync();
                    });
            }
            return Task.CompletedTask;
        }

        [Command("setdescription")]
        [Summary("Changes the description for the race")]
        public Task CommandSetDescription([Remainder][Summary("Description for the race channel")] string description)
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            ulong RaceId = GetRaceId(Context.Channel.Name);
            //get the race information from the database
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);

            //we need to check to see if the user has permission to cancel this race
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList();
            bool userHasPermission = false;

            //check to see if the user is a moderator first.
            foreach (SocketRole item in userRoles)
            {
                if (item.Name.ToLower() == "moderator")
                {
                    userHasPermission = true;
                    break;
                }
            }

            //if the user is not a moderator and they are the owner of the race, they can still set the description it if it's open for entry.
            if (!userHasPermission && race.Owner == Context.User.Id)
            {
                if (race.Status == "Entry Open") userHasPermission = true;
            }
           
            //If the user isn't allowed to use this command, return
            if (!userHasPermission)
            {
                database.Dispose();
                return Task.CompletedTask;
            }

            //Clean the description, then set the new description.
            return Task.Factory.StartNew(
                () =>
                {
                    string cleanedDescription = CleanDescription(description);
                    database.UpdateRace(race.RaceId, Description: cleanedDescription);
                    database.Dispose();
                    _ = RaceManager.UpdateChannelTopicAsync(race.RaceId);
                    _ = ReplyAsync("Race description changed successfully.");
                });
        }

        [Command("forcestart")]
        [Summary("Force a race to start")]
        public Task CommandForceStart()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //This is a moderator-only command
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList();
            bool userHasPermission = false;

            //check to see if the user is a moderator first.
            foreach (SocketRole item in userRoles)
            {
                if (item.Name.ToLower() == "moderator")
                {
                    userHasPermission = true;
                    break;
                }
            }

            //If the user isn't allowed to use this command, let them know and return
            if (!userHasPermission) return Task.CompletedTask;

            //get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            //We can only force start races that have the Entry Open status
            if (race.Status != "Entry Open") return Task.CompletedTask;

            return Task.Factory.StartNew(() => RaceManager.BeginForceStartAsync(race));
        }

        [Command("purge")]
        [Summary("Clears the messages in a channel")]
        public Task CommandPurge()
        {
            //This is a moderator-only command
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList();
            bool userHasPermission = false;

            //If the user is a moderator, they may use this command as well
            foreach (SocketRole item in userRoles)
            {
                if (item.Name.ToLower() == "moderator")
                {
                    userHasPermission = true;
                    break;
                }
            }

            //return if the user doesn't have permission to use the command
            if (!userHasPermission) return Task.CompletedTask;

            SocketTextChannel currentChannel = (SocketTextChannel)Context.Guild.GetChannel(Context.Channel.Id);
            return Task.Factory.StartNew(() => PurgeChannelAsync(currentChannel));
        }

        [Command("refresh")]
        [Summary("Refreshes a race channel")]
        public Task CommandRefresh()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //This is a moderator only command
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList();
            bool userHasPermission = false;

            foreach (SocketRole item in userRoles)
            {
                if (item.Name.ToLower() == "moderator")
                {
                    userHasPermission = true;
                    break;
                }
            }

            //return if the user doesn't have permission to use the command
            if (!userHasPermission) return Task.CompletedTask;

            return Task.Factory.StartNew(
                () =>
                {
                    ulong RaceId = GetRaceId(Context.Channel.Name);
                    //get the race information from the database
                    DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
                    RaceItem race = database.GetRaceInformation(RaceId);
                    database.Dispose();

                    _ = RaceManager.UpdateChannelTopicAsync(RaceId);
                    if (race.Status == "Entry Open") _ = RaceManager.AttemptRaceStartAsync(race);
                    else if (race.Status == "In Progress") _ = RaceManager.AttemptRaceFinishAsync(race);
                });            
        }

        private ulong GetRaceId(string channelName) => Convert.ToUInt64(channelName.Remove(0, 5));

        //This method is used to purge a channel asyncronously (to hopefully prevent blocking issues)
        private async Task PurgeChannelAsync(SocketTextChannel channel)
        {
            var oldMessages = await channel.GetMessagesAsync().FlattenAsync();
            
            //DeleteMessagesAsync doesn't work if any of the messages are older than two weeks
            TimeSpan twoWeeks = new TimeSpan(13, 23, 59, 59);

            //We'll filter out the messages that are too old to purge
            var messagesToDelete =
                from msg in oldMessages
                where (DateTime.Now - msg.Timestamp) < twoWeeks
                select msg;

            while (oldMessages.Count() != 0)
            {                
                await channel.DeleteMessagesAsync(messagesToDelete);
                oldMessages = await channel.GetMessagesAsync().FlattenAsync();
            }
        }

        private string CleanDescription(string description)
        {
            string cleanedString = description.Replace("\n", " ");
            if (cleanedString.Length > 50) cleanedString = cleanedString.Substring(0, 47) + "...";

            return cleanedString;
        }
        
    }
}
