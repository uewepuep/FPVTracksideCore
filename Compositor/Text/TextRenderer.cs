using Composition.Input;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Tools;

namespace Composition.Text
{
    public enum Scale
    {
        Can,
        Force,
        Disallowed
    }

    public interface ITextRenderer : IDisposable
    {
        Size TextSize { get; }
        float FontPoint { get; }

        bool CanCreateTextures { get;  }

        void Reset();

        void CreateGeometry(int width, int height, string text, Style style);
        void CreateTextures(Drawer drawer);

        int HitCharacterIndex(Point point);

        Point CharacterPosition(int index);

        void Draw(Drawer id, Rectangle target, RectangleAlignment alignment, Scale scale, Color tint, float alpha);
        void Draw(Drawer id, RectangleF target, RectangleAlignment alignment, Scale scale, Color tint, float alpha);
        void Draw(Drawer id, Rectangle target, RectangleAlignment alignment, Scale scale, Color tint, Vector2 origin, float rotation);
        Texture2D TextToTexture(GraphicsDevice graphicsDevice, Color textColor, string font, float point, string text);

        void SavePNG(string filename);
    }



}
