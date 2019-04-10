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
        public async Task StartRaceAsync([Remainder][Summary("Description for the race channel")] string description)
        {
            //RaceBot should only handle this command if it comes from #racebot
            if (!(Context.Channel.Id == Globals.RacebotChannelId)) return;

            string cleanDescription = _CleanDescription(description);                       

            await RaceManager.NewRaceAsync(cleanDescription, Context.User.Id);
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
            DatabaseHandler.RaceItem race = DatabaseHandler.GetRaceInformation(RaceId);

            //Verify that the race is still open to entry          
            if (race.Status != "Entry Open")
            {
                await ReplyAsync(Context.User.Mention + ", this race isn't open for entries anymore. Find a different one or start your own in the <#" + Globals.RacebotChannelId + "> channel.");
                return;
            }           

            await RaceManager.AddEntrantAsync(race, Context.User.Id);
        }
        
        [Command("ready")]
        [Summary("Sets a racer's status to 'ready'")]
        public async Task ReadyAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            //This command is only available when the race is open for entry
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler.RaceItem race = DatabaseHandler.GetRaceInformation(RaceId);
            if (race.Status != "Entry Open")
            {
                await ReplyAsync(Context.User.Mention + ", you can't use that command right now.");
                return;
            }            

            await RaceManager.SetEntrantStatusAsync(race, Context.User.Id, "Ready");
        }

        [Command("notready")]
        [Summary("Sets a racer's status to 'notready'")]
        public async Task NotReadyAsync()
        {

            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            //This command is only available when the race is open for entry
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler.RaceItem race = DatabaseHandler.GetRaceInformation(RaceId);
            if (race.Status != "Entry Open")
            {
                await ReplyAsync(Context.User.Mention + ", you can't use that command right now.");
                return;
            }

            await RaceManager.SetEntrantStatusAsync(race, Context.User.Id, "Not Ready");
        }

        [Command("done")]
        [Summary("Used when a racer has completed the race goal")]
        public async Task DoneAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            ulong RaceId = GetRaceId(Context.Channel.Name);

            //Make sure the race is in progress
            DatabaseHandler.RaceItem race = DatabaseHandler.GetRaceInformation(RaceId);
            if (race.Status != "In Progress")
            {
                await ReplyAsync(Context.User.Mention + ", you can't use that command right now.");
                return;
            }

            await RaceManager.MarkEntrantDoneAsync(race, Context.User.Id);
        }

        [Command("quit")]
        [Summary("Removes the user from the race. If the race has started, it will be recorded as a forfeit.")]
        public async Task QuitAsync()
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId))
            {
                await ReplyAsync(Context.User.Mention + ", you can't use that command right now.");
                return;
            }
            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler.RaceItem race = DatabaseHandler.GetRaceInformation(RaceId);

            if (race.Status == "Entry Open") _ = RaceManager.RemoveEntrantAsync(race, Context.User.Id);
            else if (race.Status == "In Progress") _ = RaceManager.ForfeitEntrantAsync(race, Context.User.Id);
            else await ReplyAsync(Context.User.Mention + ", you can't use that command right now.");
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

            //we need to check to see if the user has permission to cancel this race
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList<SocketRole>();
            DatabaseHandler.RaceItem race = DatabaseHandler.GetRaceInformation(RaceId);
            bool userHasPermission = false;

            //if the user is the race owner, they can use this command
            if (race.Owner == Context.User.Id) userHasPermission = true;
            else
            {
                //If the user is a moderator, they may use this command as well
                foreach (SocketRole item in userRoles)
                {
                    if (item.Name.ToLower() == "moderator")
                    {
                        userHasPermission = true;
                        break;
                    }
                }
            }

            //If the user isn't allowed to use this command, let them know and return
            if (!userHasPermission)
            {
                await ReplyAsync(user.Mention + ", only moderators and the race owner can use this command.");
                return;
            }
            //users can only cancel "Entry Open" or "In Progress" races
            if (race.Status == "Entry Open" || race.Status == "In Progress")
            {
                await RaceManager.DeleteRaceAsync(race, "Aborted");
                _ = RaceManager.UpdateRacesChannelAsync();
            }
            else await ReplyAsync(user.Mention + ", you can't use that command right now.");
        }

        [Command("setdescription")]
        [Summary("Changes the description for the race")]
        public async Task SetDescriptionAsync([Remainder][Summary("Description for the race channel")] string description)
        {
            //We can't process this message if it's not in a race channel, so we need to make sure it's coming from one
            SocketTextChannel messageChannel = (SocketTextChannel)Context.Client.GetChannel(Context.Channel.Id);
            if (!(messageChannel.CategoryId == Globals.RacesCategoryId)) return;

            ulong RaceId = GetRaceId(Context.Channel.Name);

            //we need to check to see if the user has permission to cancel this race
            var user = Context.Guild.GetUser(Context.User.Id);
            List<SocketRole> userRoles = user.Roles.ToList<SocketRole>();
            DatabaseHandler.RaceItem race = DatabaseHandler.GetRaceInformation(RaceId);
            bool userHasPermission = false;

            //if the user is the race owner, they can use this command as long as the race is open
            if (race.Owner == Context.User.Id)
            {
                if(race.Status == "Entry Open") userHasPermission = true;
                else
                {
                    await ReplyAsync(Context.User.Mention + ", you can't change the race description anymore. Contact a moderator if you really need to change the race description");
                    return;
                }
            }
            else
            {
                //If the user is a moderator, they may use this command as well
                foreach (SocketRole item in userRoles)
                {
                    if (item.Name.ToLower() == "moderator")
                    {
                        userHasPermission = true;
                        break;
                    }
                }
            }

            //If the user isn't allowed to use this command, let them know and return
            if (!userHasPermission)
            {
                await ReplyAsync(user.Mention + ", only moderators and the race owner can use this command.");
                return;
            }
            //Clean the description, then set the new description.
            string cleanedDescription = _CleanDescription(description);
            DatabaseHandler.UpdateRace(race.RaceId, Description: cleanedDescription);
            _ = RaceManager.UpdateChannelTopicAsync(race.RaceId);
            await ReplyAsync("Race description changed successfully.");
        }

        #pragma warning disable 1998
        [Command("purge")]
        [Summary("Clears the messages in a channel")]
        public async Task PurgeAsync()
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
            if (!userHasPermission) return;

            SocketTextChannel currentChannel = (SocketTextChannel)Context.Guild.GetChannel(Context.Channel.Id);

            _ = _PurgeChannelAsync(currentChannel);
        }
        #pragma warning restore 1998

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
            if (!userHasPermission) return;

            ulong RaceId = GetRaceId(Context.Channel.Name);
            DatabaseHandler.RaceItem Race = DatabaseHandler.GetRaceInformation(RaceId);

            await RaceManager.UpdateChannelTopicAsync(RaceId);
            if (Race.Status == "Entry Open") _ = RaceManager.AttemptRaceStartAsync(Race);
            else if (Race.Status == "In Progress") _ = RaceManager.AttemptRaceFinishAsync(Race);
        }

        private ulong GetRaceId(string channelName) => Convert.ToUInt64(channelName.Remove(0, 5));

        //This method is used to purge a channel asyncronously (to hopefully prevent blocking issues)
        private async Task _PurgeChannelAsync(SocketTextChannel channel)
        {
            var oldMessages = await channel.GetMessagesAsync().FlattenAsync();

            while (oldMessages.Count() != 0)
            {
                await channel.DeleteMessagesAsync(oldMessages);
                oldMessages = await channel.GetMessagesAsync().FlattenAsync();
            }
        }

        private string _CleanDescription(string description)
        {
            string cleanedString = description.Replace("\n", " ");
            if (cleanedString.Length > 50) cleanedString = cleanedString.Substring(0, 47) + "...";

            return cleanedString;
        }
        
    }
}
