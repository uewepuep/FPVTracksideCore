using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;

namespace Composition.Input
{
    public abstract class InputEvent
    {
        public DateTime Creation { get; private set; }

        public InputEvent()
        {
            Creation = DateTime.Now;
        }

    }
}
