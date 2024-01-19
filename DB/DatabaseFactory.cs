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

        public DatabaseFactory(DirectoryInfo directoryInfo, DatabaseTypes databaseType = DatabaseTypes.JSON)
        {
            this.DatabaseType = databaseType;
            Lite.LiteDatabase.Init(directoryInfo);
        }

        public RaceLib.IDatabase Open(Guid eventId)
        {
            RaceLib.IDatabase db = null;

            if (DatabaseType == DatabaseTypes.JSON)
            {
                db = new CollectionDatabase(new JSON.JSONDatabaseConverted());
            }
            else
            {
                db = new CollectionDatabase(new Lite.LiteDatabase());
            }

            db.Init(eventId);
            return db;
        }

        public RaceLib.IDatabase OpenLegacyLoad(Guid eventId)
        {
            RaceLib.IDatabase db = null;

            if (DatabaseType == DatabaseTypes.JSON)
            {
                db = new BothDatabase();
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
