using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Composition.Input
{
    
    public class MouseInputEvent
    {
        public MouseButtons Button { get; set; }
        public Point Position { get; set; }
        public Point OldPosition { get; set; }

        public int WheelChange { get; set; }
        public ButtonStates ButtonState { get; set; }

        public enum EventTypes
        {
            Button,
            Wheel,
            Move
        }

        public EventTypes EventType { get; private set; }

        public DateTime Creation { get; private set; }

        public Point Translation { get; set; }

        public Point ScreenPosition { get { return Position - Translation; } }

        public MouseInputEvent(MouseInputEvent mouseInputEvent, Point translation)
        {
            Creation = mouseInputEvent.Creation;
            Button = mouseInputEvent.Button;
            Position = mouseInputEvent.Position + translation;
            OldPosition = mouseInputEvent.OldPosition + translation;
            WheelChange = mouseInputEvent.WheelChange;
            ButtonState = mouseInputEvent.ButtonState;
            EventType = mouseInputEvent.EventType;
            Translation = translation + mouseInputEvent.Translation;
        }

        public MouseInputEvent(ButtonStates buttonState, MouseButtons button, Point position)
        {
            Creation = DateTime.Now;

            ButtonState = buttonState;
            Button = button;
            Position = position;
            EventType = EventTypes.Button;
            Translation = Point.Zero;
        }

        public MouseInputEvent(int wheelChange, Point position)
        {
            Creation = DateTime.Now;
            
            WheelChange = wheelChange;
            Button = MouseButtons.Wheel;
            Position = position;
            EventType = EventTypes.Wheel;
            Translation = Point.Zero;
        }

        public MouseInputEvent(Point newPosition, Point oldPosition)
        {
            Creation = DateTime.Now;
            
            Button = MouseButtons.None;
            OldPosition = oldPosition;
            Position = newPosition;
            Translation = Point.Zero;
            EventType = EventTypes.Move;
        }

        protected MouseInputEvent(MouseInputEvent bee)
        {
            Creation = bee.Creation;

            WheelChange = bee.WheelChange;
            Button = bee.Button;
            OldPosition = bee.OldPosition;
            Position = bee.Position;
            Translation = Point.Zero;
            EventType = bee.EventType;
        }
    }

    public class MouseInputEnterEvent : MouseInputEvent
    {
        public MouseInputEnterEvent(MouseInputEvent mie)
            : base(mie)
        {
            Button = MouseButtons.None;
            ButtonState = ButtonStates.None;
        }
    }

    public class MouseInputLeaveEvent : MouseInputEvent
    {
        public MouseInputLeaveEvent(MouseInputEvent mie)
               : base(mie)
        {
            Button = MouseButtons.None;
            ButtonState = ButtonStates.None;
        }
    }
}
