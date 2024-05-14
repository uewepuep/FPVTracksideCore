using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;

namespace UI
{
    public class EventSelectorLayer : CompositorLayer
    {
        public event Action<BaseObjectEditorNode<Event>> OnOK;

        public EventSelectorEditor Editor { get; private set; }


        public EventSelectorLayer(GraphicsDevice device, EventSelectorEditor eventSelectorEditor)
            : base(device)
        {
            Editor = eventSelectorEditor;
            
            Editor.OnOK += Editor_OnOK;
            Editor.GeneralSettingsSaved += () =>
            {
                if (LayerStack.Game is UI.BaseGame)
                {
                    ((UI.BaseGame)LayerStack.Game).Restart();
                }
            };

            Root.AddChild(Editor);
        }

        public override void SetLayerStack(LayerStack layerStack)
        {
            base.SetLayerStack(layerStack);

            //Move stuff around for streamer mode.
            BackgroundLayer backgroundLayer = LayerStack.GetLayer<BackgroundLayer>();
            if (backgroundLayer != null)
            {
                backgroundLayer.Uncrop();
            }
        }

        private void Editor_OnOK(BaseObjectEditorNode<Event> obj)
        {
            LayerStack?.Remove(this);
            Dispose();

            OnOK?.Invoke(obj);
        }
    }
}
