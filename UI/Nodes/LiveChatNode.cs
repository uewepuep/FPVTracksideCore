using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class LiveChatNode : AnimatedRelativeNode
    {
        public WebsiteNode WebsiteNode { get; private set; }
        public ColorNode Background { get; private set; }

        public TimeSpan RefreshTime { get; set; }

        private FileInfo cssFile;
        private TextNode instructionsNode;

        public LiveChatNode()
        {
            RefreshTime = TimeSpan.FromSeconds(10);

            

            instructionsNode = new TextNode("Right click here to set the URL", Theme.Current.LeftPilotList.Text.XNA);
            instructionsNode.Alignment = RectangleAlignment.Center;
            instructionsNode.RelativeBounds = new RectangleF(0.05f, 0.4f, 0.8f, 0.05f);
            AddChild(instructionsNode);

            if (!string.IsNullOrEmpty(GeneralSettings.Instance.LiveChatURL))
            {
                LoadURL(GeneralSettings.Instance.LiveChatURL);
            }
        }

        public override void SetCompositorLayer(CompositorLayer compositor)
        {
            base.SetCompositorLayer(compositor);

            DirectoryInfo working = compositor.PlatformTools.WorkingDirectory;

            cssFile = new FileInfo(Path.Combine(working.FullName, @"data/livechat.css"));
            if (!cssFile.Exists)
            {

                File.WriteAllText(cssFile.FullName, @"
#input-panel
{
    display: none;
}");
            }
        }

        public override void Update(GameTime gameTime)
        {
            if (WebsiteNode != null)
            {
                if (WebsiteNode.UpdateTime + RefreshTime < DateTime.Now && !WebsiteNode.IsGenerating && !string.IsNullOrEmpty(GeneralSettings.Instance.LiveChatURL))
                {
                    if (WebsiteNode.URL.OriginalString != GeneralSettings.Instance.LiveChatURL)
                    {
                        LoadURL(GeneralSettings.Instance.LiveChatURL);
                    } 
                    else
                    {
                        Refresh();
                    }
                }
            }

            base.Update(gameTime);
        }

        public override void Layout(Rectangle parentBounds)
        {
            base.Layout(parentBounds);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Right && mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                MouseMenu mm = new MouseMenu(this);
                mm.AddItem("Refresh", Refresh);
                mm.AddItem("Paste new URL", URLFromClipboard);

                mm.Show(mouseInputEvent);
                return true;
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        public void URLFromClipboard()
        {
            string newURL = PlatformTools.Clipboard.GetText();
            GeneralSettings.Instance.LiveChatURL = newURL;
            GeneralSettings.Write();

            LoadURL(newURL);
        }


        public void LoadURL(string newURL)
        {
            try
            {
                if (WebsiteNode != null)
                {
                    WebsiteNode.Dispose();
                    WebsiteNode = null;
                }

                WebsiteNode = new WebsiteNode(cssFile);
                WebsiteNode.KeepAspectRatio = true;
                WebsiteNode.Alignment = RectangleAlignment.Center;
                WebsiteNode.URL = new Uri(newURL);
                AddChild(WebsiteNode);

                int length = 25;
                if (newURL.Length > length)
                {
                    newURL = newURL.Substring(0, length);
                }

                instructionsNode.Text = "Loading " + newURL + "...";

                Refresh();

                RequestLayout();
            }
            catch (Exception e)
            {
                Logger.AllLog.LogException(this, e);
                if (WebsiteNode != null)
                {
                    WebsiteNode.Dispose();
                    WebsiteNode = null;
                }

            }
        }

        public void Refresh()
        {
            if (WebsiteNode != null)
            {
                WebsiteNode.Generate();
            }
        }
    }
}
