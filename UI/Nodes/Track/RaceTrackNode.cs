using Composition.Input;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThreeDee.Entities;
using Microsoft.Xna.Framework.Content;
using Composition.Layers;
using ThreeDee.Nodes;
using ImageServer;
using Tools;

namespace UI.Nodes.Track
{
    public class RaceTrackNode : RenderEntityNode
    {
        public enum Modes
        {
            SpinCenter,
            FlyThrough,
            AboveThrough,

            Selected,
        }

        protected float modeValue;
        protected Vector3 modeLookFrom;
        protected Vector3 modeLookAt;

        public Modes Mode { get; protected set; }

        public TrackEntity TrackEntity { get; private set; }

        public ContentManager ContentManager
        {
            get
            {
                return CompositorLayer.LayerStack.Content;
            }
        }

        public RaceTrackNode()
        {
            modeValue = 0;
            Mode = Modes.SpinCenter;
        }

        public override void Update(GameTime gameTime)
        {
            if (TrackEntity != null && TrackEntity.FlightPath != null) 
            {
                if (TrackEntity.FlightPath.Length == 0)
                {
                    Mode = Modes.SpinCenter;
                }

                if (float.IsNaN(modeValue))
                {
                    modeValue = 0;
                }

                float distance = modeValue;
                float next = distance + 1f;

                float speed = FlyThroughSpeed(distance, next);

                switch (Mode)
                {
                    case Modes.SpinCenter:
                        modeValue += (float)gameTime.ElapsedGameTime.TotalSeconds / 2;
                        LookAtFromAboveSpin(modeLookAt, new Vector3(0, 10, 20));
                        break;

                    case Modes.Selected:
                        LookAtFromAboveSpin(modeLookAt, new Vector3(0, 5, 10));
                        break;

                    case Modes.FlyThrough:
                        modeValue += speed * (float)gameTime.ElapsedGameTime.TotalSeconds;

                        Vector3 from = TrackEntity.FlightPath.GetPoint(distance);
                        Vector3 to = TrackEntity.FlightPath.GetPoint(next);

                        Camera?.LookAt(from, to, Vector3.Up);
                        break;

                    case Modes.AboveThrough:
                        modeValue += speed / 2 * (float)gameTime.ElapsedGameTime.TotalSeconds;

                        Vector3 lookAt = TrackEntity.FlightPath.GetPoint(modeValue);

                        Vector3 target = lookAt + new Vector3(0, 3, 0);

                        modeLookFrom += (target - modeLookFrom) * 0.5f * (float)gameTime.ElapsedGameTime.TotalSeconds;

                        Camera?.LookAt(modeLookFrom, lookAt, Vector3.Up);
                        break;

                }
            }
            base.Update(gameTime);
        }

        private float FlyThroughSpeed(float distance, float next)
        {
            const int minSpeed = 4;

            Vector3 fromTangent = TrackEntity.FlightPath.GetTangent(distance);
            Vector3 toTangent = TrackEntity.FlightPath.GetTangent(next);

            float dot = Math.Abs(Vector3.Dot(fromTangent, toTangent));
            if (float.IsNaN(dot)) 
            {
                return minSpeed;
            }

            float lerpedDot = MathHelper.Lerp(dot, dot * dot, 0.5f);
            lerpedDot *= 9;

            return Math.Max(lerpedDot, minSpeed);
        }

        private void LookAtFromAboveSpin(Vector3 lookAt, Vector3 distance)
        {
            Vector3 lookFrom = distance;
            lookFrom = Vector3.Transform(lookFrom, Matrix.CreateFromAxisAngle(Vector3.Up, modeValue));
            Camera?.LookAt(lookFrom + lookAt, lookAt, Vector3.Up);
        }

        public void ToggleMode()
        {
            modeValue = 0;
            modeLookFrom = Vector3.Zero;
            switch (Mode)
            {
                case Modes.AboveThrough:
                    Mode = Modes.FlyThrough;
                    break;

                case Modes.FlyThrough:
                    Mode = Modes.SpinCenter;
                    break;

                case Modes.SpinCenter:
                    Mode = Modes.AboveThrough;
                    break;
            }

            switch (Mode) 
            {
                case Modes.AboveThrough:
                    TrackEntity.FlightPath.Visible = true;
                    modeLookFrom = TrackEntity.FlightPath.GetPoint(-5);
                    break;

                case Modes.FlyThrough:
                    TrackEntity.FlightPath.Visible = false;
                    break;

                case Modes.SpinCenter:
                    TrackEntity.FlightPath.Visible = true;
                    break;
                case Modes.Selected:
                    TrackEntity.FlightPath.Visible = true;
                    break;
            }
        }

        public virtual void Load(RaceLib.Track track)
        {
            TrackEntity = new TrackEntity(GraphicsDevice, ContentManager);
            Root = TrackEntity;

            foreach (var v in track.TrackElements)
            {
                TrackElement tr = AddTrackElement(v.ElementType, v.Position);
                tr.Tilt = v.Tilt;
                tr.RotationTopdown = v.Rotation;
                tr.Visible = v.Visible;
                tr.Decorative = v.Decorative;
            }

            Camera.LookAt(new Vector3(0, 10, 20), Vector3.Zero);
        }

        public TrackElement AddTrackElement(RaceLib.TrackElement.ElementTypes type, Vector3 position)
        {
            TrackElement tr;
            switch (type)
            {
                case RaceLib.TrackElement.ElementTypes.Dive:
                    tr = new Dive(ContentManager);
                    break;
                case RaceLib.TrackElement.ElementTypes.Gate:
                    tr = new Gate(ContentManager);
                    break;
                case RaceLib.TrackElement.ElementTypes.Flag:
                    tr = new Flag(ContentManager);
                    break;
                case RaceLib.TrackElement.ElementTypes.Up:
                    tr = new Up(ContentManager);
                    break;
                default:
                    throw new NotImplementedException();
            }
            tr.Position = position;
            TrackEntity.AddElement(tr);
            return tr;
        }

        public IEnumerable<RaceLib.TrackElement> GetTrackElements()
        {
            foreach (TrackElement trackElement in TrackEntity.TrackElements)
            {
                RaceLib.TrackElement created = new RaceLib.TrackElement();
                created.Position = trackElement.Position;
                created.Tilt = trackElement.Tilt;
                created.Rotation = trackElement.RotationTopdown;
                created.Visible = trackElement.Visible;
                created.Decorative = trackElement.Decorative;

                if (trackElement is Gate)
                    created.ElementType = RaceLib.TrackElement.ElementTypes.Gate;

                if (trackElement is Dive)
                    created.ElementType = RaceLib.TrackElement.ElementTypes.Dive;

                if (trackElement is Up)
                    created.ElementType = RaceLib.TrackElement.ElementTypes.Up;

                if (trackElement is Flag)
                    created.ElementType = RaceLib.TrackElement.ElementTypes.Flag;

                if (created.ElementType != RaceLib.TrackElement.ElementTypes.Invalid)
                {
                    yield return created;
                }
            }
        }
    }
}
