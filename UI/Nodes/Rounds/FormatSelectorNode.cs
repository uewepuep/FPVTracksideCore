using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using RaceLib.Format;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;
using UI.Nodes;
using static RaceLib.Round;

namespace UI.Nodes.Rounds
{
    public class FormatOption
    {
        public string Name { get; }
        public string Description { get; }
        public string Author { get; }
        public string Category { get; }
        public Action Execute { get; }

        public FormatOption(string name, string description, string author, string category, Action execute)
        {
            Name = name;
            Description = description;
            Author = author;
            Category = category;
            Execute = execute;
        }
    }

    public class FormatOptionNode : SearchableNode<FormatOption>
    {
        public FormatOptionNode(FormatOption option) : base(option)
        {
        }

        public override void Init()
        {
            base.Init();
            textButtonNode.Text = "";

            TextNode nameNode = new TextNode(Value.Name, Theme.Current.InfoPanel.Text.XNA);
            nameNode.RelativeBounds = new RectangleF(0.02f, 0, 0.75f, 0.55f);
            nameNode.Alignment = RectangleAlignment.CenterLeft;
            textButtonNode.AddChild(nameNode);

            TextNode categoryNode = new TextNode(Value.Category, Theme.Current.InfoPanel.Text.XNA);
            categoryNode.RelativeBounds = new RectangleF(0, 0, 0.97f, 0.55f);
            categoryNode.Alignment = RectangleAlignment.CenterRight;
            categoryNode.Scale(0.75f);
            textButtonNode.AddChild(categoryNode);

            if (!string.IsNullOrEmpty(Value.Description))
            {
                TextNode descriptionNode = new TextNode("Description: " + Value.Description, Theme.Current.InfoPanel.Text.XNA);
                descriptionNode.RelativeBounds = new RectangleF(0.02f, 0.55f, 0.75f, 0.45f);
                descriptionNode.Alignment = RectangleAlignment.CenterLeft;
                descriptionNode.Scale(0.8f);
                textButtonNode.AddChild(descriptionNode);
            }
            
            if (!string.IsNullOrEmpty(Value.Author))
            {
                TextNode authorNode = new TextNode("Author: " + Value.Author, Theme.Current.InfoPanel.Text.XNA);
                authorNode.RelativeBounds = new RectangleF(0, 0.55f, 0.97f, 0.45f);
                authorNode.Alignment = RectangleAlignment.CenterRight;
                authorNode.Scale(0.8f);
                textButtonNode.AddChild(authorNode);
            }
        }

        public override string SearchString()
        {
            return Value.Name + " " + Value.Category + " " + Value.Description + " " + Value.Author;
        }

        public override string DisplayString()
        {
            return Value.Name;
        }

        public override string ToString()
        {
            return SearchString();
        }
    }

    public class FormatSelectorNode : SearchSelectorNode<FormatOptionNode, FormatOption>
    {
        public FormatSelectorNode(
            EventManager eventManager,
            Round round,
            IEnumerable<Pilot> orderedPilots,
            Action<SheetFormatManager.SheetFile, IEnumerable<Pilot>> onSheetSelected,
            Action<LuaFormatManager.ScriptFile, IEnumerable<Pilot>> onScriptSelected,
            Action<Round, StageTypes, IEnumerable<Pilot>> onStageSelected)
            : base("Select Format")
        {
            List<FormatOption> options = new List<FormatOption>();

            foreach (SheetFormatManager.SheetFile sheet in eventManager.RoundManager.SheetFormatManager.Sheets)
            {
                SheetFormatManager.SheetFile sheet2 = sheet;
                options.Add(new FormatOption(sheet.Name, sheet.Pilots + " pilots", "", "Spreadsheet", () => onSheetSelected(sheet2, orderedPilots)));
            }

            foreach (LuaFormatManager.ScriptFile script in eventManager.RoundManager.LuaFormatManager.GetScriptFiles())
            {
                LuaFormatManager.ScriptFile script2 = script;
                string scriptDesc = script.Description ?? "";
                if (script.HasStandings)
                    scriptDesc += (scriptDesc.Length > 0 ? " · " : "") + "Generates results";
                options.Add(new FormatOption(script.Name, scriptDesc, script.Author ?? "", "Script", () => onScriptSelected(script2, orderedPilots)));
            }

            foreach (StageTypes stageType in Enum.GetValues<StageTypes>().Except([StageTypes.Default]))
            {
                StageTypes local = stageType;
                options.Add(new FormatOption(stageType.ToString().CamelCaseToHuman(), "", "", "Native", () => onStageSelected(round, local, orderedPilots)));
            }

            SetValues(options);
            OnSelected += (FormatOption option) => option.Execute();
        }

        public override int ItemHeight()
        {
            return 40;
        }
    }
}
