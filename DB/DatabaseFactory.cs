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

        public RaceLib.IDatabase Open()
        {
            if (DatabaseType == DatabaseTypes.JSON)
            {
                return new CollectionDatabase(new JSON.JsonDatabase());
            }
            else
            {
                return new CollectionDatabase(new Lite.LiteDatabase());
            }
        }

        public RaceLib.IDatabase OpenLegacyLoad()
        {
            if (DatabaseType == DatabaseTypes.JSON)
            {
                return new BothDatabase();
            }
            else
            {
                return new CollectionDatabase(new Lite.LiteDatabase());
            }
        }
    }
}
