using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class LogNode : Node
    {
        private ListNode<TextNode> list;

        public Logger Log { get; private set; }

        private ColorNode background;

        public LogNode(Logger log) 
        {
            Alpha = 0.9f;
            Log = log;

            background = new ColorNode(Color.Black);
            background.Alpha = 0.15f;
            AddChild(background);

            list = new ListNode<TextNode>(Color.White);
            list.ItemHeight = 16;
            AddChild(list);

            log.FlushRecentHistory(AppendToLog);

            log.LogEvent += AppendToLog;

            list.RequestLayout();
            list.RequestRedraw();
        }

        private void AppendToLog(string obj)
        {
            string[] lines = obj.Split('\n');

            foreach (string line in lines)
            {
                Append(line);
            }

        }

        public override void Dispose()
        {
            Log.LogEvent -= AppendToLog;
            base.Dispose();
        }

        public void Append(string logitem)
        {
            TextNode textNode = new TextNode(logitem, Color.White);
            textNode.Alignment = RectangleAlignment.CenterLeft;
            textNode.OverrideHeight = list.ItemHeight - 2;
            list.AddChild(textNode);

            while (list.ChildCount * list.ItemHeight > Bounds.Height * 0.9f)
            {
                Node n = list.GetChild(0);
                n?.Dispose();
            }

            list.RequestLayout();
            list.RequestRedraw();
            RequestRedraw();
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            return false;
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            return false;
        }
    }
}
