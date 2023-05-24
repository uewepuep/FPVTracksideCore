using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Composition;
using System.Threading;
using UI.Nodes;
using RaceLib;
using System.IO;
using Tools;
using UI.Sponsor;

namespace UI
{
    public class TestLayer : CompositorLayer
    {
        public EventLayer EventLayer { get; set; }
        public PopupLayer PopupLayer { get; set; }

        private ListNode<TextButtonNode> buttons;
        private TextNode testingNode;

        private Thread testThread;
        private System.Action test;
        private bool runTest;

        private TextNode frameRateNode;

        private DateTime lastXFrame;
        private int frameCount;
        private TimeSpan frameMeasurementPeriod;
        private List<TextNode> debugText;
        private int timerFrameCount;
        public TestLayer(GraphicsDevice device, PopupLayer popupLayer)
            :base(device)
        {
            debugText = new List<TextNode>();

            frameMeasurementPeriod = TimeSpan.FromSeconds(1);
            PopupLayer = popupLayer;

            testThread = new Thread(RunTests);
            testThread.Name = "Test Thread";
            testThread.Start();
            runTest = true;

            AlphaFlashyNode alphaFlashyNode = new AlphaFlashyNode();
            alphaFlashyNode.FlashCycles = 40000;
            alphaFlashyNode.Flash();
            Root.AddChild(alphaFlashyNode);

            testingNode = new TextNode("Testing", Theme.Current.TextMain.XNA);
            testingNode.RelativeBounds = new RectangleF(0.45f, 0.45f, 0.1f, 0.1f);
            testingNode.Visible = false;
            alphaFlashyNode.AddChild(testingNode);

            frameRateNode = new TextNode("10 FPS", Theme.Current.TextMain.XNA);
            frameRateNode.RelativeBounds = new RectangleF(0, 0.98f, 1, 0.02f);
            frameRateNode.Visible = false;
            frameRateNode.Alignment = RectangleAlignment.TopLeft;
            Root.AddChild(frameRateNode);

            buttons = new ListNode<TextButtonNode>(Theme.Current.ScrollBar.XNA);
            buttons.RelativeBounds = new RectangleF(0.6f, 0, 0.4f, 1);
            Root.AddChild(buttons);

            //TextButtonNode abortButton = new TextButtonNode("Abort Test", Style.Instance.Button.XNA, Style.Instance.ButtonHover.XNA, Style.Instance.TextLight.XNA);
            //abortButton.OnClick += (m2ie) => { runTest = false; };
            //buttons.AddChild(abortButton);

            foreach (var methodInfo in GetType().GetMethods())
            {
                if (methodInfo.Name.StartsWith("Test"))
                {
                    TextButtonNode testButton = new TextButtonNode(methodInfo.Name.Replace("Test",""), Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
                    testButton.OnClick += (mie) =>
                    {
                        test = () =>
                        {
                            methodInfo.Invoke(this, null);
                        };
                    };
                    buttons.AddChild(testButton);
                }
            }
        }

        public override void Dispose()
        {
            runTest = false;
            base.Dispose();
        }

        private void RunTests()
        {
            while(runTest)
            {
                System.Action action = test;

                if (action != null)
                {
                    testingNode.Visible = true;
                    action();
                    testingNode.Visible = false;
                }

                if (action == test)
                    test = null;
                Thread.Sleep(10);
            }
        }

        public void TestEventOK()
        {
            TriggerButton(PopupLayer, "OkButton");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        public void TestStartRace()
        {
            if (EventLayer == null)
            {
                TestEventOK();
                Thread.Sleep(TimeSpan.FromSeconds(4));
            }

            Node node = EventLayer.Root.GetNodeByName<Node>("PilotListNode");
            MouseInputEvent mie = new MouseInputEvent(ButtonStates.Released, MouseButtons.Left, new Point(0, 0));

            Random r = new Random();

            foreach (var pilotchannelnode in node.Children.OfType<PilotChannelNode>().OrderBy(n => r.NextDouble()))
            {
                pilotchannelnode.PilotNameNode.ButtonNode.Click(mie);
            }

            Thread.Sleep(TimeSpan.FromSeconds(1));

            // Start race
            TriggerButton(EventLayer, "StartRace");
        }

        public void TestRunRace()
        {
            TestStartRace();

            WaitForRaceToEnd();

            StopRace();
        }

        private void WaitForRaceToEnd()
        {
            while (true && runTest)
            {
                Thread.Sleep(10);

                if (EventLayer.EventManager.RaceManager.RaceType == RaceLib.EventTypes.Race)
                {
                    if (EventLayer.EventManager.RaceManager.HasFinishedAllLaps())
                        break;
                }
                else
                {
                    if (EventLayer.EventManager.RaceManager.RemainingTime <= TimeSpan.Zero)
                        break;
                }
            }
        }

        private void StopRace()
        {
            Thread.Sleep(TimeSpan.FromSeconds(2));

            //stop race
            TriggerButton(EventLayer, "StopRace");

            Thread.Sleep(TimeSpan.FromSeconds(5));

            //Clear race
            //TriggerButton(EventLayer, "ClearRace");
            //Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        public void TestRun10Races()
        {
            for (int i = 0; i < 10 && runTest; i++)
            {
                TestRunRace();
            }
        }

        public void TestRun50Races()
        {
            for (int i = 0; i < 50 && runTest; i++)
            {
                TestRunRace();
            }
        }

        public void TestRunThroughRounds()
        {
            if (EventLayer == null)
            {
                TestEventOK();
                Thread.Sleep(TimeSpan.FromSeconds(4));
            }

            RaceLib.Race next = EventLayer.EventManager.RaceManager.GetNextRace(true);

            while (next != null && runTest)
            {
                EventLayer.EventManager.RaceManager.SetRace(next);

                // Start race
                TriggerButton(EventLayer, "StartRace");

                WaitForRaceToEnd();

                StopRace();

                Thread.Sleep(TimeSpan.FromSeconds(5));

                next = EventLayer.EventManager.RaceManager.GetNextRace(true);
            }
        }

        public void TestClearDiagnosticTimers()
        {
            timerFrameCount = 0;
            DebugTimer.Clear();
        }

        public void TestSponsorPopup()
        {
            SponsorLayer sponsorLayer = LayerStack.GetLayer<SponsorLayer>();
            if (sponsorLayer != null)
            {
                sponsorLayer.Trigger();
            }
        }

        private void TriggerButton(CompositorLayer layer, string nodeName)
        {
            MouseInputEvent mie = new MouseInputEvent(ButtonStates.Released, MouseButtons.Left, new Point(0,0));

            ButtonNode buttonNode = layer.Root.GetNodeByName<ButtonNode>(nodeName);
            if (buttonNode != null)
            {
                buttonNode.Click(mie);
                return;
            }

            TextButtonNode textButtonNode = layer.Root.GetNodeByName<TextButtonNode>(nodeName);
            if (textButtonNode != null)
            {
                textButtonNode.ButtonNode.Click(mie);
                return;
            }

            ImageButtonNode imageButtonNode = layer.Root.GetNodeByName<ImageButtonNode>(nodeName);
            if (imageButtonNode != null)
            {
                imageButtonNode.ButtonNode.Click(mie);
                return;
            }


            IconButtonNode iconButtonNode = layer.Root.GetNodeByName<IconButtonNode>(nodeName);
            if (iconButtonNode != null)
            {
                iconButtonNode.ButtonNode.Click(mie);
                return;
            }

            throw new Exception("Can't find " + nodeName);
        }

        protected override void OnDraw()
        {
            base.OnDraw();

            if (frameRateNode.Visible)
            {
                timerFrameCount++;

                DateTime now = DateTime.Now;
                frameCount++;

                if (now - frameMeasurementPeriod > lastXFrame)
                {
                    int frameRate = (int)(frameCount / (now - lastXFrame).TotalSeconds);
                    frameRateNode.Text = frameRate + " FPS";
                    lastXFrame = now;
                    frameCount = 0;
                }

                string[] debugTimes = DebugTimer.GetDebugTimeString(timerFrameCount).ToArray();

                for (int i = debugText.Count; i < debugTimes.Length; i++)
                {
                    TextNode node = new TextNode("test", Theme.Current.TextMain.XNA);
                    node.RelativeBounds = new RectangleF(0, frameRateNode.RelativeBounds.Y - frameRateNode.RelativeBounds.Height * (i + 1), 1, 0.02f);
                    node.Alignment = frameRateNode.Alignment;
                    Root.AddChild(node);

                    debugText.Add(node);
                    RequestLayout();
                }

                for (int i = 0; i < debugText.Count && i < debugTimes.Length; i++)
                {
                    debugText[i].Text = debugTimes[i];
                }
            }
        }

        protected override void OnUpdate(GameTime gameTime)
        {
            if (InputEventFactory.IsKeyDown(Keys.F12))
            {
                buttons.Visible = true;
            }
            else
            {
                buttons.Visible = false;
            }

            base.OnUpdate(gameTime);
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            if (inputEvent.Key == Keys.F11 && inputEvent.ButtonState == ButtonStates.Pressed)
            {
                frameRateNode.Visible = !frameRateNode.Visible;

                for (int i = 0; i < debugText.Count; i++)
                {
                    debugText[i].Visible = frameRateNode.Visible;
                }
            }

            if (inputEvent.Key == Keys.F10 && inputEvent.ButtonState == ButtonStates.Pressed)
            {
                TestClearDiagnosticTimers();
                foreach (Node n in debugText) 
                {
                    n.Dispose();
                }
                debugText.Clear();
            }

            return base.OnKeyboardInput(inputEvent);
        }
    }
}
