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
        }

        [Browsable(false)]
        public ColumnTypes Type { get; set; }
        public bool Enabled { get; set; }

        public ExportColumn()
        {
            Enabled = true;
        }

        public ExportColumn(ColumnTypes type)
            :this()
        {
            Type = type;
        }

        public override string ToString()
        {
            return Type.ToString();
        }

        private static string filename = @"data/ExportSettings.xml";
        public static ExportColumn[] Read()
        {
            List<ExportColumn> columns = new List<ExportColumn>(); ;
            try
            {
                columns.AddRange(IOTools.Read<ExportColumn>(filename));
            }
            catch
            {
            }

            foreach (ColumnTypes type in Enum.GetValues(typeof(ColumnTypes)))
            {
                if (!columns.Any(c => c.Type == type))
                {
                    columns.Add(new ExportColumn(type));
                }
            }

            Write(columns.ToArray());
            return columns.ToArray();
        }

        public static void Write(ExportColumn[] s)
        {
            IOTools.Write(filename, s);
        }
    }
}
