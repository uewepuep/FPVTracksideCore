using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;
using Tools;

namespace UI.Nodes
{
    public class WelcomeSetupNode : Node
    {
        public event System.Action OnOK;
        public event System.Action Restart;

        public Node secondContainerNode { get; private set; }

        private MenuButton menuButton;

        public WelcomeSetupNode(Texture2D logo, Profile profile)
        {
            Scale(0.5f, 0.9f);

            BorderPanelShadowNode background = new BorderPanelShadowNode();
            AddChild(background);
            SetBack(background);

            ColorNode colorNode = new ColorNode(Theme.Current.TopPanel.XNA);
            colorNode.RelativeBounds = new RectangleF(0, 0, 1, 0.21f);
            background.AddChild(colorNode);

            ImageNode logoNode = new ImageNode(logo);
            logoNode.Alignment = RectangleAlignment.TopCenter;
            colorNode.AddChild(logoNode);

            TextCheckBoxNode continueNeverShow = new TextCheckBoxNode("Don't show this again", Theme.Current.TextMain.XNA, false);
            continueNeverShow.RelativeBounds = new Tools.RectangleF(0.45f, 0.94f, 0.25f, 0.025f);
            continueNeverShow.Checkbox.ValueChanged += Checkbox_ValueChanged;
            background.AddChild(continueNeverShow);

            TextButtonNode okButtonNode = new TextButtonNode("Continue", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            okButtonNode.RelativeBounds = new Tools.RectangleF(0.75f, 0.925f, 0.2f, 0.05f);
            background.AddChild(okButtonNode);

            okButtonNode.OnClick += Continue;

            Node content = new Node();
            content.RelativeBounds = new RectangleF(0.1f, 0.23f, 0.8f, 0.67f);
            background.AddChild(content);

            float oneItem = 0.07f;
            float padding = oneItem * 0.75f;
            float currentY = 0;
            float buttonItem = 0.1f;


            TextNode welcome = new TextNode("Welcome!", Theme.Current.TextMain.XNA);
            welcome.RelativeBounds = new Tools.RectangleF(0, currentY, 1, oneItem);
            content.AddChild(welcome);

            currentY = welcome.RelativeBounds.Bottom;
            currentY += padding;

            TextNode p1 = new TextNode("Thank you for using FPVTrackside!\n" +
                "First thing we want you to be aware of us our online manual.\n" +
                "It goes through a lot of the setup and explains some of the terminology we use.", Theme.Current.TextMain.XNA);
            p1.Alignment = RectangleAlignment.CenterLeft;
            p1.RelativeBounds = new Tools.RectangleF(0, currentY, 1, oneItem * 1.5f);
            content.AddChild(p1);

            currentY = p1.RelativeBounds.Bottom;
            currentY += padding;

            Node buttonContainer1 = new Node();
            buttonContainer1.RelativeBounds = new Tools.RectangleF(0, currentY, 1, buttonItem);
            content.AddChild(buttonContainer1);

            TextButtonNode manual = new TextButtonNode("Online\nManual", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            manual.OnClick += Manual_OnClick;
            buttonContainer1.AddChild(manual);

            currentY = buttonContainer1.RelativeBounds.Bottom;
            currentY += padding;

            AlignHorizontally(0.0f, null, null, manual, null, null);

            TextNode p2 = new TextNode("Next the main things you'll need to setup before you can get going are\naccessible through the buttons below..", Theme.Current.TextMain.XNA);
            p2.Alignment = RectangleAlignment.CenterLeft;
            p2.RelativeBounds = new Tools.RectangleF(0, currentY, 1, oneItem);
            content.AddChild(p2);

            currentY = p2.RelativeBounds.Bottom;
            currentY += padding;

            Node settingsButtonContainer = new Node();
            settingsButtonContainer.RelativeBounds = new RectangleF(0, currentY, 1, buttonItem);
            content.AddChild(settingsButtonContainer);

            TextButtonNode themeSettings = new TextButtonNode("Theme\nSelection", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            themeSettings.OnClick += ThemeSettings_OnClick;
            settingsButtonContainer.AddChild(themeSettings);

            TextButtonNode videoSettings = new TextButtonNode("Video\nSettings", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            videoSettings.OnClick += VideoSettings_OnClick;
            settingsButtonContainer.AddChild(videoSettings);

            AlignHorizontally(0.1f, null, themeSettings, videoSettings, null);

            currentY = settingsButtonContainer.RelativeBounds.Bottom;
            currentY += padding;

            secondContainerNode = new Node();
            secondContainerNode.RelativeBounds = new RectangleF(0, currentY, 1, buttonItem);
            content.AddChild(secondContainerNode);

            TextButtonNode channelSettings = new TextButtonNode("Channel\nSettings", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            channelSettings.OnClick += ChannelSettings_OnClick;
            secondContainerNode.AddChild(channelSettings);

            TextButtonNode timingSettings = new TextButtonNode("Timing\nSettings", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            timingSettings.OnClick += TimingSettings_OnClick;
            secondContainerNode.AddChild(timingSettings);

            AlignHorizontally(0.1f, null, channelSettings, timingSettings, null);

            currentY = secondContainerNode.RelativeBounds.Bottom;
            currentY += padding;

            TextNode p4 = new TextNode("Great! That's the basics done. After leaving this screen these settings\ncan be accessed by the menu in the top right.", Theme.Current.TextMain.XNA);
            p4.Alignment = RectangleAlignment.CenterLeft;
            p4.RelativeBounds = new RectangleF(0, currentY, 1, oneItem);
            content.AddChild(p4);

            menuButton = new MenuButton(profile, Color.White, Color.White);
            menuButton.Visible = false;
            menuButton.Restart += MenuButton_Restart;
            content.AddChild(menuButton);
        }

        private void MenuButton_Restart(RaceLib.Event obj)
        {
            Restart?.Invoke();
        }

        private void ChannelSettings_OnClick(MouseInputEvent mie)
        {
            menuButton.ShowChannelSettings();
        }

        private void Checkbox_ValueChanged(bool obj)
        {
            ApplicationProfileSettings.Instance.ShowWelcomeScreen = !obj;
            ApplicationProfileSettings.Write();
        }

        private void ThemeSettings_OnClick(Composition.Input.MouseInputEvent mie)
        {
            menuButton.ShowThemeSettings();
        }
        private void Manual_OnClick(Composition.Input.MouseInputEvent mie)
        {
            menuButton.ShowOnlineManual();
        }

        private void TimingSettings_OnClick(Composition.Input.MouseInputEvent mie)
        {
            menuButton.ShowTimingSettings();
        }

        private void VideoSettings_OnClick(Composition.Input.MouseInputEvent mie)
        {
            menuButton.ShowVideoSettings();
        }

        private void Continue(Composition.Input.MouseInputEvent mie)
        {
            OnOK?.Invoke();
        }
    }
}
