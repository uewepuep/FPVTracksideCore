using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace Spreadsheets
{
    public class SheetFormat : IDisposable
    {
        private FileInfo excelFile;

        private const string sheetname = "fpvtrackside";

        private Dictionary<string, int> headingColumnMap;

        private List<string> pilots;

        private ISheet sheet;

        public int Channels { get; private set; }
        public string Name { get; private set; }

        public bool LockChannels { get; private set; }

        public bool CreateBrackets { get; private set; }

        public SheetFormat(string filename)
            :this(new FileInfo(filename))
        {
        }
        public SheetFormat(FileInfo file)
        {
            if (!file.Exists)
            {
                return;
            }

            Name = file.Name.Replace(file.Extension, "").Replace("_", " ");

            excelFile = file;

            headingColumnMap = new Dictionary<string, int>();
            Channels = 0;
            pilots = new List<string>();

            sheet = new OpenSheet();
            sheet.Open(excelFile, sheetname);
            int i = 1;
            foreach (string heading in sheet.GetRowText(1))
            {
                string lower = heading.ToLower();
                if (!headingColumnMap.ContainsKey(lower))
                {
                    headingColumnMap.Add(lower, i);
                }
                i++;
            }

            int number;

            pilots = new List<string>();
            if (headingColumnMap.TryGetValue("pilots", out number))
            {
                foreach (var cell in sheet.GetColumnText(number).Skip(1))
                {
                    if (!string.IsNullOrEmpty(cell))
                    {
                        pilots.Add(cell);
                    }
                }
            }

            List<string> settings = sheet.GetColumnText(1).Select(c => c.ToLower()).ToList();

            string channels = GetSetting(settings, "channels");
            if (channels != null)
            {
                int channel;
                if (int.TryParse(channels, out channel))
                {
                    Channels = channel;
                }
            }

            LockChannels = GetBoolSetting(settings, "lock channels");
            CreateBrackets = GetBoolSetting(settings, "create brackets");
        }

        public bool GetBoolSetting(List<string> settings, string name)
        {
            string temp = GetSetting(settings, name);
            if (temp != null)
            {
                bool tempBool;
                if (bool.TryParse(temp, out tempBool))
                {
                    return tempBool;
                }
                int tempInt;
                if (int.TryParse(temp, out tempInt))
                {
                    return tempInt > 0;
                }
            }

            return false;
        }

        public string GetSetting(List<string> settings, string name)
        {
            int index = settings.IndexOf(name);
            if (index >= 0 && index + 1 < settings.Count)
            {
                return settings[index + 1];
            }
            return null;
        }

        public void Dispose()
        {
            sheet.Dispose();
            sheet = null;
        }

        public IEnumerable<string> GetPilots()
        {
            return pilots;
        }


        public IEnumerable<string> GetFirstRoundPilots()
        {
            int pilotColumn;
            if (headingColumnMap.TryGetValue("pilots", out pilotColumn))
            {
                int firstRound = pilotColumn + 1;
                foreach (var cell in sheet.GetColumnText(firstRound).Skip(1))
                {
                    if (!string.IsNullOrEmpty(cell))
                    {
                        yield return cell;
                    }
                }
            }
        }

        public IEnumerable<string> GetRounds()
        {
            return headingColumnMap.Keys.Where(r => r.Contains("round"));
        }

        private int GetColumn(params string[] heading)
        {
            if (heading.Length == 1)
            {
                int number;
                if (headingColumnMap.TryGetValue(heading.First().ToLower(), out number))
                {
                    return number;
                }
                return 0;
            }

            foreach (var kvp in headingColumnMap)
            {
                bool containsAll = true;
                foreach (string he in heading)
                {
                    if (!kvp.Key.Contains(he.ToLower()))
                    {
                        containsAll = false;
                        break;
                    }
                }

                if (containsAll)
                {
                    return kvp.Value;
                }
            }

            return 0;
        }


        private int GetColumn(string eventType, int round)
        {
            return GetColumn("Round", eventType, round.ToString());
        }

        public IEnumerable<SheetRace> GetRaces(string eventType, int round)
        {
            int column = GetColumn(eventType, round);
            if (column > 0)
            {
                int raceStartIndex = 2;
                int raceNumber = 1;
                bool hasData = false;
                List<SheetPilotChannel> sheetPilotChannels;
                do
                {
                    hasData = false;

                    sheetPilotChannels = new List<SheetPilotChannel>();
                    for (int i = 0; i < Channels; i++)
                    {
                        string pilotName = sheet.GetText(raceStartIndex + i, column);

                        if (!string.IsNullOrEmpty(pilotName))
                            hasData = true;

                        if (pilots.Contains(pilotName))
                        {
                            sheetPilotChannels.Add(new SheetPilotChannel(pilotName, i));
                        }
                    }

                    if (hasData)
                    {
                        yield return new SheetRace(eventType, round, raceNumber, sheetPilotChannels);
                        raceNumber++;
                        raceStartIndex += Channels;
                    }
                }
                while (hasData);
            }
        }

        public bool SetResults(string eventType, int round, int race, IEnumerable<SheetResult> results)
        {
            int nameColumn = GetColumn(eventType, round);
            int resultColumn = nameColumn + 1;
            if (nameColumn > 0)
            {
                int raceRowStart = Channels * (race - 1) + 2;
                foreach (var sr in results)
                {
                    string pilotName = sr.Pilot;
                    for (int i = 0; i < Channels; i++)
                    {
                        string nameCell = sheet.GetText(raceRowStart + i, nameColumn);
                        if (nameCell == pilotName)
                        {
                            sheet.SetValue(raceRowStart + i, resultColumn, sr.Value);
                            break;
                        }
                    }
                }
            }

            sheet.Calculate();

            return true;
        }

        public void GetSize(out int rows, out int columns)
        {
            rows = 0; 
            columns = 0;

            int i = 1;
            IEnumerable<string> row;
            do
            {
                row = sheet.GetRowText(i);
                i++;

                if (row.Any())
                {
                    rows++;
                    columns = Math.Max(columns, row.Count());
                }
            }
            while (row.Any());
        }

        public string GetCellText(int r, int c)
        {
            return sheet.GetText(r, c);
        }

        public bool IsPilotColumn(int columnNumber)
        {
            string name = headingColumnMap.Where(kvp => kvp.Value == columnNumber).Select(k => k.Key).FirstOrDefault();
            if (string.IsNullOrEmpty(name))
                return false;
            return IsPilotColumn(name);
        }

        public bool IsPilotColumn(string columnName)
        {
            string columnNameLower = columnName.ToLower();
            return columnNameLower.Contains("pilot") || columnNameLower.Contains("round");
        }

        public bool IsResultColumn(int columnNumber)
        {
            //Result Columns are always one to the right of pilot columns
            return IsPilotColumn(columnNumber - 1);
        }

        public void Save(string fileName, Dictionary<string, string> pilotNameMap, IEnumerable<string> validRoundTypes)
        {
            string[] lowerValidTypes = validRoundTypes.Select(x => x.ToLower()).ToArray();

            FileInfo file = new FileInfo(fileName);

            if (file.Exists)
            {
                file.Delete();
            }

            excelFile.CopyTo(file.FullName);

            ISheet sheet2 = new OpenSheet();
            sheet2.Open(file, "FPVTrackside");

            foreach (var column in headingColumnMap)
            {
                int columnNumber = column.Value;

                // List is column 2.
                bool isPilotList = columnNumber == 2;

                bool hasRoundNumber = false;
                bool hasWordRound = column.Key.Contains("round");
                bool hasType = false;
                string[] split = column.Key.Split(" ");
                foreach (string word in split)
                {
                    int roundNumber;
                    if (int.TryParse(word, out roundNumber))
                    {
                        if (roundNumber > 0 && roundNumber < 1000)
                        {
                            hasRoundNumber = true;
                        }
                    }

                    if (lowerValidTypes.Contains(word))
                    {
                        hasType = true;
                    }
                }

                bool isPilotRaces = hasRoundNumber && (hasWordRound || hasType);

                if (isPilotList || isPilotRaces)
                {
                    string[] pilotColumn = sheet.GetColumnText(columnNumber).ToArray();
                    for (int i = 0; i < pilotColumn.Length; i++)
                    {
                        int row = i + 1;

                        string name;
                        if (pilotNameMap.TryGetValue(pilotColumn[i], out name))
                        {
                            sheet2.SetValue(row, columnNumber, name);
                            continue;
                        }
                    }
                }

                if (isPilotRaces)
                {
                    // Results are always one column more..
                    columnNumber += 1;

                    string[] resultColumn = sheet.GetColumnText(columnNumber).ToArray();

                    for (int i = 0; i < resultColumn.Length; i++)
                    {
                        int row = i + 1;

                        int value;
                        if (int.TryParse(resultColumn[i], out value))
                        {
                            sheet2.SetValue(row, columnNumber, value);
                        }
                        else
                        {
                            sheet2.SetValue(row, columnNumber, resultColumn[i]);
                        }
                    }
                }
            }
            sheet2.Save();
        }
    }

    public class SheetRace
    {
        public string EventType { get; set; }
        public int Round { get; set; }

        public int Number { get; set; }

        public SheetPilotChannel[] PilotChannels { get; set; }

        public SheetRace(string eventType, int round, int number, IEnumerable<SheetPilotChannel> pilots)
        {
            EventType = eventType;
            Round = round;
            Number = number;
            PilotChannels = pilots.ToArray();
        }

        public override string ToString()
        {
            return EventType + " " + Round + "-" + Number + " [" + PilotChannels.Count() + "]";
        }
    }

    public class SheetPilotChannel
    {
        public string Pilot { get; set; }
        public int ChannelSlot { get; set; }

        public SheetPilotChannel(string pilot, int channel)
        {
            this.Pilot = pilot;
            this.ChannelSlot = channel;
        }

        public override string ToString()
        {
            return Pilot + " " + ChannelSlot;
        }
    }

    public class SheetResult : SheetPilotChannel
    {
        public object Value { get; set; }

        public SheetResult(string pilot, int channel, object value)
            :base(pilot, channel)
        {
            this.Value = value;
        }

        public override string ToString()
        {
            return base.ToString() + " " + Value;
        }
    }
}
