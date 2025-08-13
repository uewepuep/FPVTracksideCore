using RaceLib.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace DB.JSON
{
    public class Stage : DatabaseObjectT<RaceLib.Stage>
    {
        public string Name { get; set; }
        public int Order { get; set; }

        public Stage() { }

        public Stage(RaceLib.Stage obj)
            : base(obj)
        {
            
        }

        public override RaceLib.Stage GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Stage stage = new RaceLib.Stage();
            ReflectionTools.Copy(this, stage);
            return stage;
        }


}
