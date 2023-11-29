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
        public DatabaseFactory(DirectoryInfo directoryInfo)
        {
            Lite.LiteDatabase.Init(directoryInfo);
        }

        public RaceLib.IDatabase Open()
        {
            return new CollectionDatabase(new Lite.LiteDatabase());
            //return new CollectionDatabase(new JSON.JsonDatabase(this));
        }
    }
}
