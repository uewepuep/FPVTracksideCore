using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class LanguageButtonNode : TextButtonNode
    {
        public event Action<string> OnLanguageSet;

        public LanguageButtonNode(Color background, Color hover, Color textColor)
            : base("Language: " + ApplicationProfileSettings.Instance.Language, background, hover, textColor)
        {
            TextNode.Alignment = RectangleAlignment.CenterLeft;
            OnClick += LanguageButtonNode_OnClick;
        }

        private void LanguageButtonNode_OnClick(Composition.Input.MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.LeftToRight = false;

            foreach (Translator translator in TranslatorFactory.Load())
            {
                string name = translator.Language;
                mouseMenu.AddItem(name, () => { OnLanguageSet(name); });
            }
            mouseMenu.Show(this);
        }
    }
}
