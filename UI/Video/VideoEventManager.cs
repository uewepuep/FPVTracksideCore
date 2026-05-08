using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Video
{
    public class VideoEventManager : EventManager
    {
        public VideoManager VideoManager { get; set; }  

        public VideoEventManager(Profile profile) : base(profile)
        {
        }

        public override bool HasReplay(Race race)
        {
            return VideoManager.HasReplay(race);
        }
    }
}
