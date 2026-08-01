using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Sound;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Nodes
{
    public class SubtitleNode : AlphaAnimatedNode
    {
        public SoundManager SoundManager { get; set; }

        public event Action EnabledChanged;

        private bool enabled;
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled == value)
                    return;

                enabled = value;
                ApplicationProfileSettings.Instance.ShowSubtitles = value;
                ApplicationProfileSettings.Write();

                EnabledChanged?.Invoke();
            }
        }

        private DateTime expires;
        private bool timedOut;

        private TextNode textNode;

        public SubtitleNode(SoundManager soundManager)
        {
            SoundManager = soundManager;
            enabled = ApplicationProfileSettings.Instance.ShowSubtitles;

            Alpha = 0;
            timedOut = true;

            soundManager.OnSpeech += SoundManager_OnSpeech;

            ColorNode colorNode = new ColorNode(Theme.Current.SubtitleBackground);
            AddChild(colorNode);

            textNode = new TextNode("", Theme.Current.SubtitleText.XNA);
            textNode.Scale(0.9f);
            textNode.Style.Italic = true;

            AddChild(textNode);
        }

        private void SoundManager_OnSpeech(string speech)
        {
            if (!speech.EndsWith("."))
                speech += '.';

            textNode.Text = speech;
            expires = DateTime.Now + TimeSpan.FromSeconds(ApplicationProfileSettings.Instance.SubtitleTimeoutSeconds);
            timedOut = false;
            SetAnimatedAlpha(1);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!timedOut && DateTime.Now > expires)
            {
                timedOut = true;
                SetAnimatedAlpha(0);
            }
        }
    }
}
