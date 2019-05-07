using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace Discord_RaceBot
{
    /*
    public class RequireRoleAttribute : PreconditionAttribute
    {
        private readonly string _roleName;

        public RequireRoleAttribute(string roleName)
        {
            _roleName = roleName;
        }

        public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var user = context.User as SocketGuildUser;

            if()
        }
    }
    */

    public class RequireCategoryAttribute : PreconditionAttribute
    {
        private readonly string _categoryName;

        public RequireCategoryAttribute(string categoryName)
        {
            _categoryName = categoryName;
        }

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            //Get the channel as a SocketTextChannel so we can check its category
            var channel = (SocketTextChannel)context.Channel;
            
            //If the channel's category name matches, then we can go ahead with the command
            if (channel.Category.Name.ToLower() == _categoryName.ToLower()) return Task.FromResult(PreconditionResult.FromSuccess());
            else return Task.FromResult(PreconditionResult.FromError("This command cannot be used in this channel."));
        }
    }
}
