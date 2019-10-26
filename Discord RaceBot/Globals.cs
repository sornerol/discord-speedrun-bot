using System.Xml;
using System.Collections.Generic;

namespace Discord_RaceBot
{
    static class Globals
    {
        public const string Version = "2019.10.26";
        public static ulong RacesChannelId;
        public static ulong RacebotChannelId;
        public static ulong RacesCategoryId;
        public static ulong GuildId;
        public static string Token;
        public static string MySqlConnectionString;     

        public static void LoadGlobalsFromConfigFile()
        {
            XmlReader reader = XmlReader.Create("config.xml");
            //we'll temporarily store the values from the XML file in a Dictionary
            Dictionary<string, string> GlobalsList = new Dictionary<string, string>();

            //Read in all the values
            while (reader.Read())
            {
                if(reader.NodeType == XmlNodeType.Element && reader.Name == "item") GlobalsList.Add(reader.GetAttribute("name"), reader.GetAttribute("value"));
            }
                        
            reader.Close();
            reader.Dispose();

            //Transfer the values in the dictionary to their respective properties
            RacesChannelId = ulong.Parse(GlobalsList["RacesChannelId"]);
            RacebotChannelId = ulong.Parse(GlobalsList["RacebotChannelId"]);
            RacesCategoryId = ulong.Parse(GlobalsList["RacesCategoryId"]);
            GuildId = ulong.Parse(GlobalsList["GuildId"]);
            Token = GlobalsList["Token"];
            MySqlConnectionString = GlobalsList["MySqlConnectionString"];

        }
    }

    
}
