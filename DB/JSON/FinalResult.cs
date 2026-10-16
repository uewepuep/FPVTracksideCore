using System;

namespace DB.JSON
{
    public class FinalResult : DatabaseObjectT<RaceLib.FinalResult>
    {
        public Guid Pilot { get; set; }
        public int? Position { get; set; }
        public Guid Stage { get; set; }
        public bool Pinned { get; set; }

        public FinalResult() { }

        public FinalResult(RaceLib.FinalResult obj)
            : base(obj)
        {
            if (obj.Pilot != null)
                Pilot = obj.Pilot.ID;
            if (obj.Stage != null)
                Stage = obj.Stage.ID;
        }

        public override RaceLib.FinalResult GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.FinalResult result = base.GetRaceLibObject(database);

            result.Pilot = Pilot.Convert<RaceLib.Pilot>(database);
            if (Stage != Guid.Empty)
                result.Stage = Stage.Convert<RaceLib.Stage>(database);

            return result;
        }
    }
}
