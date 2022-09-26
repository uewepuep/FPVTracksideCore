using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Nodes
{
    public class TableNode : Node
    {
        public float PaddingHorizontal { get; set; }
        public float PaddingVertical { get; set; }

        public int Columns { get; private set; }
        public int Rows { get; private set; }

        public int CellCount { get { return Columns * Rows; } }

        public IEnumerable<Node> Cells
        {
            get
            {
                for (int r = 0; r < ChildCount && r < Rows; r++)
                {
                    Node row = GetChild(r);
                    if (row != null)
                    {
                        for (int c = 0; c < row.ChildCount && c < Columns; c++)
                        {
                            yield return row.GetChild(c);
                        }
                    }
                }
            }
        }

        public int[] ColumnWidths { get; set; }
        public int[] RowHeights { get; set; }

        public bool AbsoluteWidths { get { return ColumnWidths != null; } }
        public bool AbsoluteHeights { get { return RowHeights != null; } }

        public TableNode()
        {
            Rows = 4;
            Columns = 4;
            PaddingHorizontal = 0.01f;
            PaddingVertical = 0.01f;
        }

        public TableNode(int rows, int columns)
            :this()
        {
            SetSize(rows, columns);
        }

        public void SetSize(int rows, int columns)
        {
            Rows = rows;
            Columns = columns;
            CreateCells();
        }

        private void CreateCells()
        {
            for (int r = 0; r < ChildCount || r < Rows; r++)
            {
                Node row = GetChild(r);
                if (row == null)
                {
                    row = new Node();
                    AddChild(row);
                }

                if (r >= Rows)
                {
                    row.Dispose();
                    r--;
                }
                else
                {
                    for (int c = 0; c < row.ChildCount || c < Columns; c++)
                    {
                        Node cell = row.GetChild(c);
                        if (cell == null)
                        {
                            cell = new Node();
                            row.AddChild(cell);
                        }

                        if (c >= Columns)
                        {
                            cell.Dispose();
                            c--;
                        }
                    }

                    if (!AbsoluteWidths)
                    {
                        AlignHorizontally(PaddingHorizontal, row.Children);
                    }
                }
            }
            if (!AbsoluteHeights)
            {
                AlignVertically(PaddingVertical, Children);
            }
            RequestLayout();
        }

        public Node GetCell(int x, int y)
        {
            if (y >= 0 && y < ChildCount)
            {
                Node row = GetChild(y);
                if (row != null)
                {
                    if (x >= 0 && x < row.ChildCount)
                    {
                        return row.GetChild(x);
                    }
                }
            }
            return null;
        }

        // Gets the cells in the normal reading order, left to right, top to bottom
        public Node GetCell(int index)
        {
            int x = index % Columns;
            int y = index / Columns;

            return GetCell(x, y);
        }
    }
}
