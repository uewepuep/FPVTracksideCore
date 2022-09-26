using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using RaceLib.Format;
using Spreadsheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Nodes
{
    public class SheetNode : TableNode
    {
        private RoundSheetFormat roundSheetFormat;

        public SheetNode(RoundSheetFormat roundSheet)
        {
            roundSheetFormat = roundSheet;
            roundSheetFormat.OnGenerate += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            int height;
            int width;

            ClearDisposeChildren();

            SheetFormat sheetFormat = roundSheetFormat.SheetFormat;
            sheetFormat.GetSize(out height, out width);

            SetSize(height, width);
            
            for (int c = 0; c < width; c++)
            {
                bool isPilotColumn = sheetFormat.IsPilotColumn(c + 1);

                for (int r = 0; r < height; r++)
                {
                    string text = sheetFormat.GetCellText(r + 1, c + 1);

                    if (isPilotColumn)
                    {
                        Pilot pilot;
                        if (roundSheetFormat.GetPilotMapped(text, out pilot))
                        {
                            text = pilot.Name;
                        }
                    }

                    Node n = GetCell(c, r);
                    BorderNode border = new BorderNode(Color.Gray);
                    n.AddChild(border);

                    if (!string.IsNullOrEmpty(text))
                    {
                        TextNode tn = new TextNode(text, Color.White);
                        tn.Scale(1, 0.5f);
                        border.AddChild(tn);
                    }
                }
            }

            RequestLayout();
        }

        public override void Dispose()
        {
            roundSheetFormat.OnGenerate -= Refresh;
            base.Dispose();
        }
    }
}
