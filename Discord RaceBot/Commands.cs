using System.Threading.Tasks;
using System;
using System.Linq;
using System.Collections.Generic;
using Discord;
using Discord.Commands;
using Discord.WebSocket;


namespace Discord_RaceBot
{
    /*
     * The CommandsModule is the implementation of commands available to users.
     */
    public class CommandsModule : ModuleBase<SocketCommandContext>
    {
        [Command("startrace")]
        [Summary("Opens a new race channel in the Discord server")]
        public Task StartRaceAsync([Remainder][Summary("Description for the race channel")] string description)
        {
            //RaceBot should only handle this command if it comes from #racebot
            if (!(Context.Channel.Id == Globals.RacebotChannelId)) return Task.CompletedTask;

            string cleanDescription = CleanDescription(description);                       

            Task.Factory.StartNew(()=> RaceManager.NewRaceAsync(cleanDescription, Context.User.Id));
            return Task.CompletedTask;
        }
        
        [Command("join")]
        [Summary("Adds the user to the race. Must be used within a race channel")]
        public async Task JoinAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            //get the RaceId by removing "race-" from the channel name we're in
            ulong RaceId = GetRaceId(Context.Channel.Name);

            //get the race information from the database
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            //Verify that the race is still open to entry          
            if (race.Status != "Entry Open") return;      

            await RaceManager.AddEntrantAsync(race, Context.User.Id);
        }
        
        [Command("ready")]
        [Summary("Sets a racer's status to 'ready'")]
        public async Task ReadyAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            //This command is only available when the race is open for entry, so we need to get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            if (race.Status != "Entry Open" && race.Status != "Countdown") return;                    

            await RaceManager.SetEntrantStatusAsync(race, Context.User.Id, "Ready");
        }

        [Command("notready")]
        [Summary("Sets a racer's status to 'notready'")]
        public async Task NotReadyAsync()
        {

            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            //This command is only available when the race is open for entry, so we need to get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            if (race.Status != "Entry Open") return;
            
            await RaceManager.SetEntrantStatusAsync(race, Context.User.Id, "Not Ready");
        }

        [Command("done")]
        [Summary("Used when a racer has completed the race goal")]
        public async Task DoneAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            //we need to get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            if (race.Status != "In Progress") return;

            await RaceManager.MarkEntrantDoneAsync(race, Context.User.Id);
        }

        [Command("notdone")]
        [Summary("Used when a racer accidentally uses the .done command")]
        public async Task NotDoneAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            //we need to get the race information from the database
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            EntrantItem entrant = database.GetEntrantInformation(RaceId, Context.User.Id);
            database.Dispose();

            //don't continue with this command if the entrant isn't marked done.
            if (entrant.Status != "Done") return;

            if (race.Status != "In Progress" && race.Status != "Recently Completed") return;

            await RaceManager.MarkEntrantNotDoneAsync(race, Context.User.Id);
        }

        [Command("quit")]
        [Summary("Removes the user from the race. If the race has started, it will be recorded as a forfeit.")]
        public Task QuitAsync()
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
            if (race.Status == "Entry Open" || race.Status == "Countdown") _ = RaceManager.RemoveEntrantAsync(race, Context.User.Id);
            else if (race.Status == "In Progress") _ = RaceManager.ForfeitEntrantAsync(race, Context.User.Id);

            return Task.CompletedTask;
        }

        [Command("time")]
        [Summary("Displays how much time has elapsed since the race began.")]
        public Task TimeAsync()
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

            _ = RaceManager.ShowTimeAsync(race);
            return Task.CompletedTask;
        }

        [Command("comment")]
        [Summary("Records a comment for the entrant.")]
        public async Task CommentAsync([Remainder][Summary("The comment to leave for this entrant")] string comment)
        {

            await ReplyAsync("This command is not implemented yet.");
        }

        [Command("cancel")]
        [Summary("Cancels the race and deletes the channels/role")]
        public async Task CancelAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            ulong RaceId = GetRaceId(Context.Channel.Name);
            //get the race information from the database
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            //we need to check to see if the user has permission to cancel this race
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList<SocketRole>();
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
            if (!userHasPermission) return;

            //users can only cancel "Entry Open" or "In Progress" races
            if (race.Status == "Entry Open" || race.Status == "In Progress")
            {
                await RaceManager.DeleteRaceAsync(race, "Aborted");
                _ = RaceManager.UpdateRacesChannelAsync();
            }
        }

        [Command("setdescription")]
        [Summary("Changes the description for the race")]
        public async Task SetDescriptionAsync([Remainder][Summary("Description for the race channel")] string description)
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            ulong RaceId = GetRaceId(Context.Channel.Name);
            //get the race information from the database
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);

            //we need to check to see if the user has permission to cancel this race
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList<SocketRole>();
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
                return;
            }

            //Clean the description, then set the new description.
            string cleanedDescription = CleanDescription(description);
            database.UpdateRace(race.RaceId, Description: cleanedDescription);
            database.Dispose();
            _ = RaceManager.UpdateChannelTopicAsync(race.RaceId);
            await ReplyAsync("Race description changed successfully.");
        }

        [Command("forcestart")]
        [Summary("Force a race to start")]
        public Task ForceStartAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return Task.CompletedTask;

            //This is a moderator-only command
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList<SocketRole>();
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

            //Start the process of force starting the race
            _ = RaceManager.BeginForceStartAsync(race);
            return Task.CompletedTask;
        }

        [Command("purge")]
        [Summary("Clears the messages in a channel")]
        public Task PurgeAsync()
        {
            //This is a moderator-only command
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList<SocketRole>();
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
            Task.Factory.StartNew(() => PurgeChannelAsync(currentChannel));
            return Task.CompletedTask;
        }

        [Command("refresh")]
        [Summary("Refreshes a race channel")]
        public async Task RefreshAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            //This is a moderator only command
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList<SocketRole>();
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
            if (!userHasPermission) return;

            ulong RaceId = GetRaceId(Context.Channel.Name);
            //get the race information from the database
            DatabaseHandler database = new DatabaseHandler(Globals.MySqlConnectionString);
            RaceItem race = database.GetRaceInformation(RaceId);
            database.Dispose();

            await RaceManager.UpdateChannelTopicAsync(RaceId);
            if (race.Status == "Entry Open") _ = RaceManager.AttemptRaceStartAsync(race);
            else if (race.Status == "In Progress") _ = RaceManager.AttemptRaceFinishAsync(race);
        }

        private ulong GetRaceId(string channelName) => Convert.ToUInt64(channelName.Remove(0, 5));

        //This method is used to purge a channel asyncronously (to hopefully prevent blocking issues)
        private async Task PurgeChannelAsync(SocketTextChannel channel)
        {
            var oldMessages = await channel.GetMessagesAsync().FlattenAsync();

            while (oldMessages.Count() != 0)
            {
                await channel.DeleteMessagesAsync(oldMessages);
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
