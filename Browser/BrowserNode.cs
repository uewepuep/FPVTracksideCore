using System;
using System.IO;
using System.Threading.Tasks;
using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Playwright;
using Microsoft.Xna.Framework;
using Tools;

namespace Browser
{
    public class BrowserNode : Node
    {
        private enum InstallState { Checking, NotInstalled, Installing, Ready }

        private string url;
        public string Url
        {
            get => url;
            set
            {
                url = value;
                if (chromiumNode != null)
                    chromiumNode.Url = value;
            }
        }

        public TimeSpan RefreshInterval
        {
            get => chromiumNode?.RefreshInterval ?? TimeSpan.FromSeconds(5);
            set { if (chromiumNode != null) chromiumNode.RefreshInterval = value; }
        }

        private Color? backgroundColor;
        public Color? BackgroundColor
        {
            get => chromiumNode?.BackgroundColor ?? backgroundColor;
            set
            {
                backgroundColor = value;
                if (chromiumNode != null)
                    chromiumNode.BackgroundColor = value;
            }
        }

        private InstallState state = InstallState.Checking;
        private bool checking;
        public bool AllowContextMenu { get; set; }

        private ChromiumNode chromiumNode;
        private ColorNode background;
        private TextNode statusText;
        private TextButtonNode installButton;

        public BrowserNode()
        {
            AllowContextMenu = false;

            background = new ColorNode(new Color(20, 20, 20));
            background.RelativeBounds = new RectangleF(0, 0, 1, 1);
            AddChild(background);

            statusText = new TextNode("Checking for Chromium...", Color.White);
            statusText.RelativeBounds = new RectangleF(0.1f, 0.38f, 0.8f, 0.12f);
            AddChild(statusText);

            installButton = new TextButtonNode("Install Chromium", new Color(30, 100, 200), new Color(60, 140, 230), Color.White);
            installButton.RelativeBounds = new RectangleF(0.3f, 0.55f, 0.4f, 0.1f);
            installButton.OnClick += (mie) => _ = InstallAsync();
            installButton.Visible = false;
            AddChild(installButton);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (state == InstallState.Checking && !checking)
            {
                checking = true;
                _ = CheckInstalledAsync();
            }

            base.Draw(id, parentAlpha);
        }

        private async Task CheckInstalledAsync()
        {
            bool installed = false;
            try
            {
                using IPlaywright pw = await Playwright.CreateAsync();
                installed = File.Exists(pw.Chromium.ExecutablePath);
            }
            catch (Exception e)
            {
                Logger.Browser.LogException(this, e);
            }

            if (installed)
                SetReady();
            else
                SetNotInstalled();
        }

        private async Task InstallAsync()
        {
            state = InstallState.Installing;
            statusText.Text = "Installing Chromium...";
            installButton.Visible = false;
            RequestRedraw();

            await Task.Run(() => Microsoft.Playwright.Program.Main(new[] { "install", "chromium" }));
            SetReady();
        }

        private void SetReady()
        {
            state = InstallState.Ready;

            background.Dispose();
            background = null;
            statusText.Dispose();
            statusText = null;
            installButton.Dispose();
            installButton = null;

            chromiumNode = new ChromiumNode();
            chromiumNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            chromiumNode.BackgroundColor = backgroundColor;
            chromiumNode.Url = url;
            AddChild(chromiumNode);

            RequestRedraw();
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (AllowContextMenu && state == InstallState.Ready &&
                mouseInputEvent.Button == MouseButtons.Right)
            {
                if (mouseInputEvent.ButtonState == ButtonStates.Released)
                    ShowContextMenu(mouseInputEvent);
                return true;
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        private void ShowContextMenu(MouseInputEvent mie)
        {
            MouseMenu menu = new MouseMenu(this);
            menu.AddItem("Refresh", () => chromiumNode?.Refresh());
            menu.AddItem("Paste URL", PasteUrl);
            menu.Show(mie);
        }

        private void PasteUrl()
        {
            string text = PlatformTools.Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(text))
                Url = text;
        }

        private void SetNotInstalled()
        {
            state = InstallState.NotInstalled;
            statusText.Text = "Chromium is not installed. (~100-200 MB download)";
            installButton.Visible = true;
            RequestRedraw();
        }
    }
}
