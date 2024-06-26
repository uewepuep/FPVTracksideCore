﻿using Composition;
using Composition.Input;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Video
{
    public class FileFrameNode : FrameNode
    {

        private IPlaybackFrameSource playbackFrameSource
        {
            get
            {
                return Source as IPlaybackFrameSource;
            }
        }

        public bool Repeat
        {
            get
            {
                if (playbackFrameSource == null)
                    return false;

                return playbackFrameSource.Repeat;
            }
            set
            {
                if (playbackFrameSource != null)
                    playbackFrameSource.Repeat = value;
            }
        }

        public FileFrameNode(FrameSource source) 
            : base(source)
        {
            Start();
        }

        public override void Dispose()
        {
            base.Dispose();

            if (Source != null)
            {
                // We need to dispose the source we created.
                Source.Dispose();
                Source = null;
            }
        }

        public void Play()
        {
            playbackFrameSource?.Play();
        }

        public bool Start()
        {
            if (playbackFrameSource == null)
                return false;

            return playbackFrameSource.Start();
        }

        public void Seek(TimeSpan timeSpan)
        {
            playbackFrameSource?.SetPosition(timeSpan);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (playbackFrameSource != null && mouseInputEvent.Button == MouseButtons.Left)
            {
                playbackFrameSource.SetPosition(TimeSpan.Zero);
                playbackFrameSource.Play();
            }

            return base.OnMouseInput(mouseInputEvent);
        }
    }

}
