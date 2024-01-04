using Composition.Nodes;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace UI.Nodes
{
    public class RaceEditor : ObjectEditorNode<Race>
    {
        private EventManager eventManager;
        public RaceEditor(EventManager eventManager, Race race, bool addRemove = true, bool cancelButton = false)
           : base(race, false, true, false)
        {
            this.eventManager = eventManager;
            Text = "Race Editor";
            OnOK += aOnOK;
        }

        private void aOnOK(BaseObjectEditorNode<Race> obj)
        {
            Race race = Single;
            if (race != null)
            {
                using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
                {
                    db.Update(race);
                }
            }
        }

        protected override PropertyNode<Race> CreatePropertyNode(Race obj, PropertyInfo pi)
        {
            if (pi.Name == "RoundNumber")
            {
                int[] rounds = obj.Event.Rounds.Select(r => r.RoundNumber).Distinct().OrderBy(i => i).ToArray();
                ListPropertyNode<Race> listPropertyNode = new ListPropertyNode<Race>(obj, pi, ButtonBackground, TextColor, ButtonHover, rounds);
                return listPropertyNode;
            }

            return base.CreatePropertyNode(obj, pi);
        }
    }
}
