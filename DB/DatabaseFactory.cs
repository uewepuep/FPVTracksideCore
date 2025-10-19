using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB
{
    public class DatabaseFactory : RaceLib.IDatabaseFactory
    {
        private DirectoryInfo EventDirectory { get; set; }

        public DatabaseFactory(DirectoryInfo LiteDBDirectory, DirectoryInfo eventDirectory)
        {

            if (!eventDirectory.Exists)
            {
                eventDirectory.Create();
            }

            EventDirectory = eventDirectory;
        }

        public RaceLib.IDatabase Open(Guid eventId)
        {
            RaceLib.IDatabase db = new CollectionDatabase(new JSON.JSONDatabaseConverted(EventDirectory));
            db.Init(eventId);
            return db;
        }
    }
}
