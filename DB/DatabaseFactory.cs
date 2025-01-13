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
        public enum DatabaseTypes
        {
            Lite,
            JSON
        }

        public DatabaseTypes DatabaseType { get; set; }

        private DirectoryInfo EventDirectory { get; set; }

        public DatabaseFactory(DirectoryInfo LiteDBDirectory, DirectoryInfo eventDirectory, DatabaseTypes databaseType = DatabaseTypes.JSON)
        {
            this.DatabaseType = databaseType;
            Lite.LiteDatabase.Init(LiteDBDirectory);

            if (!eventDirectory.Exists)
            {
                eventDirectory.Create();
            }

            EventDirectory = eventDirectory;
        }

        public RaceLib.IDatabase Open(Guid eventId)
        {
            RaceLib.IDatabase db = null;

            if (DatabaseType == DatabaseTypes.JSON)
            {
                db = new CollectionDatabase(new JSON.JSONDatabaseConverted(EventDirectory));
            }
            else
            {
                db = new CollectionDatabase(new Lite.LiteDatabase());
            }

            db.Init(eventId);
            return db;
        }
    }
}
