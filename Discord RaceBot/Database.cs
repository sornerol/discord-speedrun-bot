using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;


namespace Discord_RaceBot
{
    public class DatabaseHandler:IDisposable
    {
        //_connection stores the MySqlConnection
        private MySqlConnection _connection;
        private bool _disposed = false;
        
        //Connect with the passed connection string when the object is created
        public DatabaseHandler(string connectionString)
        {
            this._connection = new MySqlConnection(connectionString);
            try
            {
                //connect to the database
                this._connection.Open();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }
        }
        
        /* 
         * NewRace() Creates a new races with [description] belonging to [UserId]
         */
        public ulong NewRace(string description, ulong UserId)
        {
            MySqlCommand cmd;
            try
            {
                cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT INTO races(Owner,Description,Status)VALUES(@owner,@description,@status)";
                cmd.Parameters.AddWithValue("@owner", UserId);
                cmd.Parameters.AddWithValue("@description", description);
                cmd.Parameters.AddWithValue("@status", "Entry Open");
                cmd.ExecuteNonQuery();
            }
            catch(Exception)
            {
                throw;
            }
            return (ulong)cmd.LastInsertedId;
        }

        /*
         *UpdateRace(): Updates the race in the database
         */
        public bool UpdateRace(ulong RaceId,
            ulong TextChannelId = 0,
            ulong VoiceChannelId = 0,
            ulong RoleId = 0,
            string Description = null,
            string Status = null,
            string StartTime = null
            )
        {
            //First, create a list containing the requested updates. We'll use these snippets to build the update string
            List<string> fieldUpdates = new List<string>();
            if (TextChannelId != 0) fieldUpdates.Add("TextChannelId = " + TextChannelId);
            if (VoiceChannelId != 0) fieldUpdates.Add("VoiceChannelId = " + VoiceChannelId);
            if (RoleId != 0) fieldUpdates.Add("RoleId = " + RoleId);
            if (Description != null) fieldUpdates.Add("Description = '" + Description + "'");
            if (Status != null) fieldUpdates.Add("Status = '" + Status + "'");
            if (StartTime != null) fieldUpdates.Add("StartTime = '" + StartTime + "'");

            MySqlCommand cmd;
           
            try
            {
                //Build the command
                cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE races SET ";
                //itemCount is used to determine if we need to put a comma in front of a value
                int itemCount = 0;
                //append each item in the update list to the update string
                foreach(string item in fieldUpdates)
                {
                    //if this isn't the first item in the list, we need to add a comma to separate it from the previous item
                    if(itemCount > 0)
                    {
                        cmd.CommandText += ",";
                    }
                    cmd.CommandText += item;
                    itemCount++;
                }
                cmd.CommandText += " WHERE ID = " + RaceId;
                cmd.ExecuteNonQuery();                
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }

            return false;
        }
        

        /* 
         * JoinRace(): Check to see if [UserId] is entered in [RaceId]. If not, add the user to the race.
         */
        public bool JoinRace(ulong RaceId, ulong UserId)
        {
            MySqlCommand cmd;
            int cmdResult;
            //first, search to see if the user is already entered
            try
            {
                cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Count(*) FROM entries WHERE RaceId = @RaceId AND UserId = @UserId";
                cmd.Parameters.AddWithValue("@RaceId", RaceId);
                cmd.Parameters.AddWithValue("@UserId", UserId);
                cmdResult = int.Parse(cmd.ExecuteScalar() + "");
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }

            //If the query returns a result, that means the player is already entered for the race.
            if (cmdResult != 0) return true;

            //enter the player into the race.
            try
            {
                cmd.CommandText = "INSERT INTO entries(RaceId,UserId,Status)VALUES(@RaceId,@UserId,@Status)";
                cmd.Parameters.AddWithValue("@Status", "Not Ready");
                cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }

            return false;
        }

        /*
         * UpdateEntry(): Updates the status for [UserId] in [RaceId]
         */
        public bool UpdateEntry(ulong RaceId, ulong UserId, string Status)
        {
            MySqlCommand cmd;
            int result;
            try
            {
                //Build the command
                cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE entries SET Status = @Status WHERE RaceId = @RaceId AND UserId = @UserId";
                cmd.Parameters.AddWithValue("@Status", Status);
                cmd.Parameters.AddWithValue("@RaceId", RaceId);
                cmd.Parameters.AddWithValue("@UserId", UserId);
                
                result = cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message + "\n\n" + e.StackTrace);
                throw;
            }

            //If the UserId/RaceId combo exists in the entries table, result will be 1. If not, it will be 0.
            if (result > 0) return false;
            else return true;   //the update command didn't affect any rows, so we need to let the calling function know
        }

        /*
         * MarkEntrantFinished(): Sets [UserId's] status to Done and records the finish time for [RaceId].
         */
        
        public EntrantItem MarkEntrantFinished(ulong RaceId, ulong UserId, DateTime StartTime)
        {
            MySqlCommand cmd;
            int result = 0;

            TimeSpan raceTime = StartTime - DateTime.Now;

            EntrantsSummary raceSummary = GetEntrantsSummary(RaceId);
            EntrantItem entrant = null;

            int place = raceSummary.Done + 1;

            try
            {
                //Build the command
                cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE entries SET Status = 'Done',FinishedTime = @FinishedTime,Place = @Place  WHERE RaceId = @RaceId AND UserId = @UserId";
                cmd.Parameters.AddWithValue("@RaceId", RaceId);
                cmd.Parameters.AddWithValue("@UserId", UserId);
                cmd.Parameters.AddWithValue("@FinishedTime", raceTime.ToString(@"hh\:mm\:ss"));
                cmd.Parameters.AddWithValue("@Place", place);

                result = cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }
            //If the UserId/RaceId combo exists in the entries table, get the entrant summary and return it.
            if (result > 0) entrant = GetEntrantInformation(RaceId, UserId);

            return entrant;
        }

        public bool MarkEntrantNotFinished(ulong RaceId, ulong UserId)
        {
            MySqlCommand cmd;
            int result;
            
            try
            {
                //Build the command
                cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE entries SET Status = 'Ready',FinishedTime = NULL,Place = NULL  WHERE RaceId = @RaceId AND UserId = @UserId";
                cmd.Parameters.AddWithValue("@RaceId", RaceId);
                cmd.Parameters.AddWithValue("@UserId", UserId);
                result = cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }
            //If the UserId/RaceId combo exists in the entries table, the userid/raceid combo was updated.
            if (result > 0) return false;

            return true;
        }

        /*
         * GetRaceInformation(): Returns the race information for [RaceId]
         */
        public RaceItem GetRaceInformation(ulong RaceId)
        {
            MySqlCommand cmd;
            MySqlDataReader dataReader; //for reading the results of the query
            try
            {
                cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT * from races WHERE ID = @RaceId";
                cmd.Parameters.AddWithValue("@RaceId", RaceId);
                dataReader = cmd.ExecuteReader();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }
            //we should never read in more than one result, so we don't have to mess with a loop for dataReader.Read()
            dataReader.Read();

            DateTime convertedDateTime;
            if (dataReader["StartTime"] == DBNull.Value) convertedDateTime = DateTime.MinValue;
            else convertedDateTime = Convert.ToDateTime(dataReader["StartTime"]);
            RaceItem Race = new RaceItem(
                RaceId,
                (ulong)dataReader["TextChannelId"],
                (ulong)dataReader["VoiceChannelId"],
                (ulong)dataReader["RoleId"],
                (ulong)dataReader["Owner"],
                (string)dataReader["Description"],
                (string)dataReader["Status"],
                convertedDateTime);

            dataReader.Close();
            dataReader.Dispose();

            return Race;

        }

        public List<RaceItem> GetRaceList(string status)
        {

            MySqlCommand cmd;
            MySqlDataReader dataReader; //for reading the results of the query   
            List<RaceItem> raceList = new List<RaceItem>();

            try
            {
                cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT * from races";
                //determine what race statuses to return
                switch (status)
                {
                    //return races that aren't completed or aborted
                    case "allactive":
                        cmd.CommandText += " WHERE Status NOT IN('Complete','Aborted')";
                        break;
                    //return all races
                    case "all":
                        break;
                    //return races with a status of [status]
                    default:
                        cmd.CommandText += " WHERE Status = @Status";
                        cmd.Parameters.AddWithValue("@Status", status);
                        break;

                }
                dataReader = cmd.ExecuteReader();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }

            DateTime convertedDateTime;
            while (dataReader.Read())
            {
                if (dataReader["StartTime"] == DBNull.Value) convertedDateTime = DateTime.MinValue;
                else convertedDateTime = Convert.ToDateTime(dataReader["StartTime"]);
                raceList.Add(new RaceItem(
                    (ulong)dataReader["ID"],
                    (ulong)dataReader["TextChannelId"],
                    (ulong)dataReader["VoiceChannelId"],
                    (ulong)dataReader["RoleId"],
                    (ulong)dataReader["Owner"],
                    (string)dataReader["Description"],
                    (string)dataReader["Status"],
                    convertedDateTime));
            }
            dataReader.Close();
            dataReader.Dispose();

            return raceList;
        }

        public List<EntrantItem> GetEntrantList(ulong raceId, string status)
        {

            MySqlCommand cmd;
            MySqlDataReader dataReader; //for reading the results of the query   
            List<EntrantItem> entrantList = new List<EntrantItem>();

            try
            {
                cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT * from entries WHERE RaceId = @RaceId";
                cmd.Parameters.AddWithValue("@RaceId", raceId);
                //determine which entrant statuses to return

                if (status != "all")
                {
                    cmd.CommandText += " AND Status = @Status";
                    cmd.Parameters.AddWithValue("@Status", status);
                }

                dataReader = cmd.ExecuteReader();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }

            TimeSpan convertedDateTime;
            int place;
            string comment;

            while (dataReader.Read())
            {                
                if (dataReader["FinishedTime"] == DBNull.Value) convertedDateTime = TimeSpan.Zero;
                else convertedDateTime = (TimeSpan)dataReader["FinishedTime"];

                if (dataReader["Place"] == DBNull.Value) place = -1;
                else place = int.Parse(dataReader["Place"] + "");

                if (dataReader["Comment"] == DBNull.Value) comment = null;
                else comment = (string)dataReader["Comment"];

                entrantList.Add(new EntrantItem(
                    (ulong)dataReader["ID"],
                    (ulong)dataReader["RaceId"],
                    (ulong)dataReader["UserId"],
                    (string)dataReader["Status"],
                    convertedDateTime,
                    place,
                    comment));
            }
            dataReader.Close();
            dataReader.Dispose();

            return entrantList;
        }

        public bool DeleteEntrant(ulong RaceId, ulong UserId)
        {
            MySqlCommand cmd;
            int cmdResult;
            
            try
            {
                cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM entries WHERE RaceId = @RaceId AND UserId = @UserId";
                cmd.Parameters.AddWithValue("@RaceId", RaceId);
                cmd.Parameters.AddWithValue("@UserId", UserId);
                cmdResult = cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }



            //If the query returns a result, the entrant was deleted
            if (cmdResult != 0) return false;
            //The entrant wasn't deleted (probably because they're not in the race).
            else return true;
        }

        public EntrantsSummary GetEntrantsSummary(ulong RaceId)
        {
            MySqlCommand cmd;
            MySqlDataReader dataReader; //for reading the results of the query

            try
            {
                cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Status,Count(*) AS Entrants FROM entries WHERE RaceId = @RaceId GROUP BY Status";
                cmd.Parameters.AddWithValue("@RaceId", RaceId);
                dataReader = cmd.ExecuteReader();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }

            EntrantsSummary entrantsSummary = new EntrantsSummary(RaceId);
            string status;  //stores the status from the dataReader so we can decide which field in entrantsSummary to update
            int entrantsCount;


            while (dataReader.Read())
            {
                status = (string)dataReader["Status"];
                entrantsCount = int.Parse(dataReader["Entrants"] + "");
                switch (status)
                {
                    case "Not Ready":
                        entrantsSummary.NotReady = entrantsCount;
                        break;
                    case "Ready":
                        entrantsSummary.Ready = entrantsCount;
                        break;
                    case "Done":
                        entrantsSummary.Done = entrantsCount;
                        break;
                    case "Forfeited":
                        entrantsSummary.Forfeited = entrantsCount;
                        break;
                    case "Disqualified":
                        entrantsSummary.Disqalified = entrantsCount;
                        break;
                    default:    //should never trigger, but if it does for some reason, we'll just ignore the data
                        break;
                }
            }
            dataReader.Close();
            dataReader.Dispose();
            
            return entrantsSummary;
        }

        public EntrantItem GetEntrantInformation(ulong RaceId, ulong UserId)
        {
            MySqlCommand cmd;
            MySqlDataReader dataReader; //for reading the results of the query
            try
            {
                cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT * from entries WHERE RaceId = @RaceId AND UserId = @UserId";
                cmd.Parameters.AddWithValue("@RaceId", RaceId);
                cmd.Parameters.AddWithValue("@UserId", UserId);
                dataReader = cmd.ExecuteReader();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown: " + e.Message);
                throw;
            }

            //we should never read in more than one result, so we don't have to mess with a loop for dataReader.Read()

            EntrantItem entrant = null;
            
            if (dataReader.Read())
            {
                TimeSpan convertedDateTime;
                int place;
                string comment;

                if (dataReader["FinishedTime"] == DBNull.Value) convertedDateTime = TimeSpan.Zero;
                else convertedDateTime = (TimeSpan)dataReader["FinishedTime"];

                if (dataReader["Place"] == DBNull.Value) place = -1;
                else place = int.Parse(dataReader["Place"] + "");

                if (dataReader["Comment"] == DBNull.Value) comment = null;
                else comment = (string)dataReader["Comment"];

                entrant = new EntrantItem(
                    (ulong)dataReader["ID"],
                    (ulong)dataReader["RaceId"],
                    (ulong)dataReader["UserId"],
                    (string)dataReader["Status"],
                    convertedDateTime,
                    place,
                    comment);
            }

            dataReader.Close();
            dataReader.Dispose();

            return entrant;

        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            //check to see if Dispose has already been called
            if(!this._disposed)
            {
                //if disposing is true, dispose all managed and unmanaged resources
                if(disposing)
                {
                    //close and dispose of the MySqlConnection
                    _connection.Close();
                    _connection.Dispose();
                }
                //we would dispose of any unmanaged resources here

                //note that disposing has been done.
                _disposed = true;
            }
        }
    }

    /*
     * RaceItem is used to return information about a race from the database
     */
    public class RaceItem
    {
        public ulong RaceId { get; set; }
        public ulong TextChannelId { get; set; }
        public ulong VoiceChannelId { get; set; }
        public ulong RoleId { get; set; }
        public ulong Owner { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public DateTime StartTime { get; set; }

        public RaceItem(ulong _RaceId, ulong _TextChannelId, ulong _VoiceChannelId, ulong _RoleId, ulong _Owner, string _Description, string _Status, DateTime _StartTime)
        {
            RaceId = _RaceId;
            TextChannelId = _TextChannelId;
            VoiceChannelId = _VoiceChannelId;
            RoleId = _RoleId;
            Owner = _Owner;
            Description = _Description;
            Status = _Status;
            StartTime = _StartTime;
        }
    }

    /*
     * EntrantsSummary is used to return a summary of entrants' statuses
     */
    public class EntrantsSummary
    {
        public ulong RaceId { get; set; }
        public int NotReady { get; set; }
        public int Ready { get; set; }
        public int Done { get; set; }
        public int Forfeited { get; set; }
        public int Disqalified { get; set; }
        public int TotalEntrants
        {
            get
            {
                return NotReady + Ready + Done + Forfeited + Disqalified;
            }
        }

        public EntrantsSummary(ulong _RaceId)
        {
            RaceId = _RaceId;
            NotReady = 0;
            Ready = 0;
            Done = 0;
            Forfeited = 0;
            Disqalified = 0;
        }
    }

    /*
     * EntrantItem returns details on a specific entrant
     */
    public class EntrantItem
    {
        public ulong Id;
        public ulong RaceId;
        public ulong UserId;
        public string Status;
        public TimeSpan FinishedTime;
        public int Place;
        public string Comment;

        public EntrantItem(ulong Id, ulong RaceId, ulong UserId, string Status, TimeSpan FinishedTime, int Place, string Comment)
        {
            this.Id = Id;
            this.RaceId = RaceId;
            this.UserId = UserId;
            this.Status = Status;
            this.FinishedTime = FinishedTime;
            this.Place = Place;
            this.Comment = Comment;
        }
    }
}
