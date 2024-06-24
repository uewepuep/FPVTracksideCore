using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public class ExportColumn
    {
        public enum ColumnTypes
        {
            PilotName,
            Position,
            ConsecutiveLapsTime,
            FastestLapTime,
            RaceTime,
            PBTime,
            RoundNumber, 
            RaceNumber,

            Lap1Time,
            Lap2Time,
            Lap3Time,
            Lap4Time,
            Lap5Time,
            Lap6Time,

            FastestSpeed,
            AverageSpeed,
            Distance
        }

        [Browsable(false)]
        public ColumnTypes Type { get; set; }
        public bool Enabled { get; set; }

        public ExportColumn()
        {
            Enabled = true;
        }

        public ExportColumn(ColumnTypes type, bool enabled)
            :this()
        {
            Type = type;
            Enabled = enabled;
        }

        public override string ToString()
        {
            return Type.ToString();
        }

        private const string filename = "ExportSettings.xml";
        public static ExportColumn[] Read(Profile profile)
        {
            List<ExportColumn> columns = new List<ExportColumn>(); ;
            try
            {
                columns.AddRange(IOTools.Read<ExportColumn>(profile, filename));
            }
            catch
            {
            }

            ColumnTypes[] defaultEnabled = new ColumnTypes[] 
            {
                ColumnTypes.PilotName,
                ColumnTypes. Position,
                ColumnTypes.ConsecutiveLapsTime,
                ColumnTypes.FastestLapTime,
                ColumnTypes.RaceTime,
                ColumnTypes.FastestSpeed
            };

            foreach (ColumnTypes type in Enum.GetValues(typeof(ColumnTypes)))
            {
                if (!columns.Any(c => c.Type == type))
                {
                    columns.Add(new ExportColumn(type, defaultEnabled.Contains(type)));
                }
            }

            Write(profile, columns.ToArray());
            return columns.ToArray();
        }

        public static void Write(Profile profile, ExportColumn[] s)
        {
            IOTools.Write(profile, filename, s);
        }
    }
}
