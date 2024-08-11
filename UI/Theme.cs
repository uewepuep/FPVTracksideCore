using RaceLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tools;

namespace UI
{
    public class Theme
    {
        public string Filename { get { return Directory + "/theme.xml"; } }

        [XmlIgnore()]
        [Browsable(false)]
        public string Name { get; set; }
        
        [XmlIgnore()]
        [Browsable(false)]
        public DirectoryInfo Directory { get; set; }
        [XmlIgnore()]
        [Browsable(false)]
        public DateTime ReadTime { get; private set; }

        public string FontFamily { get; set; }
        public ToolTexture Background { get; set; }
        public ToolTexture FPVTracksideLogo { get; set; }

        public ToolColor TextAlt { get; set; }
        public ToolColor TextMain { get; set; }
        public ToolColor Button { get; set; }
        public ToolColor Hover { get; set; }

        public ToolTexture TopPanel { get; set; }
        public ToolColor TopPanelText { get; set; }
        public bool TopPanelTextBorder { get; set; }

        public ToolColor PanelAlt { get; set; }
        public ToolColor Panel { get; set; }

        public ToolColor ScrollBar { get; set; }
                
        public ToolColor MenuBackground { get; set; }
        public ToolColor MenuText { get; set; }
        public ToolColor MenuTextInactive { get; set; }

        public ToolColor OverallBestTime { get; set; }
        public ToolColor NewPersonalBest { get; set; }
        public ToolColor BehindTime { get; set; }
        public ToolColor AheadTime { get; set; }
        public ToolColor InCurrentRace { get; set; }

        public ToolColor[] ChannelColors { get; set; }

        public PilotTheme PilotViewTheme { get; set; }

        public BorderPanelTheme Editor { get; set; }
        public RoundPanelTheme Rounds { get; set; }
        public PanelTheme RightControls { get; set; }
        public PilotListPanelTheme LeftPilotList { get; set; }
        public InfoPanelTheme InfoPanel { get; set; }
        public BorderPanelTheme Login { get; set; }
        public PanelTheme Replay { get; set; }

        public TrackTheme TrackTheme { get; set; }

        public static Theme Current { get; private set; }

        public Theme()
        {
            Editor = new BorderPanelTheme();
            Rounds = new RoundPanelTheme();
            RightControls = new PanelTheme();
            LeftPilotList = new PilotListPanelTheme();
            InfoPanel = new InfoPanelTheme();
            Login = new BorderPanelTheme();
            Replay = new PanelTheme();

            FPVTracksideLogo = new ToolTexture(@"img\logo.png", 0, 0, 0, 0);

            PilotViewTheme = new PilotTheme();

            BehindTime = new ToolColor(255, 133, 117);
            AheadTime = new ToolColor(117, 255, 133);

            OverallBestTime = new ToolColor(240, 124, 255);
            NewPersonalBest = new ToolColor(117, 255, 133);

            ScrollBar = new ToolColor(236, 236, 236, 128);

            PilotViewTheme.PositionText = new ToolColor(255, 255, 255);
            PilotViewTheme.PilotOverlayText = new ToolColor(255, 255, 255);
            PilotViewTheme.PilotOverlayPanel = new ToolTexture(15, 15, 15, 255);
            PilotViewTheme.PilotTitleAlpha = 160;
            PilotViewTheme.PilotNameBackground = new ToolTexture(@"pilot.png", 255,255,255);
            PilotViewTheme.PBBackground = new ToolTexture(@"pbbg.png", 0,0,0);
            PilotViewTheme.NoVideoBackground = new ToolTexture(@"static.png", 22, 22, 22);
            PilotViewTheme.CrashedOut = new ToolTexture(@"crashed.png", 22, 22, 22);

            InCurrentRace = new ToolColor(255, 216, 0);

            FontFamily = "Roboto";

            ChannelColors = new ToolColor[]
            {
                new ToolColor(255, 0, 0),
                new ToolColor(0, 0, 255),
                new ToolColor(255, 255, 0),
                new ToolColor(255, 0, 255),
                new ToolColor(50, 240, 0),
                new ToolColor(255, 255, 255),
                new ToolColor(0, 240, 240),
                new ToolColor(255, 168, 0)
            };

            TrackTheme = new TrackTheme();
        }

        public static List<Theme> Themes { get; private set; }

        public static IEnumerable<Theme> Load(DirectoryInfo directoryInfo)
        {
            KeyValuePair<string, string>[] replacements = new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>("PBPage", "InfoPanel"),
            };

            foreach (DirectoryInfo directory in directoryInfo.GetDirectories("*"))
            {
                FileInfo themeFile = new FileInfo(directory.FullName + "/theme.xml");
                if (themeFile.Exists)
                {
                    Theme theme;
                    try
                    {
                        theme = IOTools.Read<Theme>(directory.FullName, themeFile.Name, replacements).FirstOrDefault();
                        theme.Name = directory.Name;
                        theme.Directory = directory;
                        theme.ReadTime = DateTime.Now;

                        theme.Repair();

                        IOTools.Write(directory.FullName, themeFile.Name, theme);

                    }
                    catch
                    {
                        continue;
                    }

                    yield return theme;
                }
            }
        }

        public static void Initialise(DirectoryInfo workingDirectory, string name)
        {
            List<Theme> themes = new List<Theme>();

            DirectoryInfo themesDirectory = new DirectoryInfo(Path.Combine(workingDirectory.FullName, "themes/"));

            themes.AddRange(Load(themesDirectory));

            Themes = themes.ToList();

            if (ApplicationProfileSettings.Instance != null)
            {
                Current = Themes.FirstOrDefault(t => t.Name == ApplicationProfileSettings.Instance.Theme);
            }

            if (Current == null)
            {
                Current = Themes.FirstOrDefault(t => t.Name == name);
            }

            if (Current == null)
            {
                Current = Themes.FirstOrDefault();
            }

            if (Current != null)
            {
                LocaliseFilenames(Current.Directory, Current);
            }
            else
            {
                Logger.UI.Log(null, "No Themes");
            }

            Composition.Text.Style.DefaultFont = Current.FontFamily;
        }

        private void Repair()
        {
            AutoName(Name, "", this);

            ForEach<ToolTexture>(this, (t) =>
            {
                if (t == null)
                {
                    return new ToolTexture("", 128, 128, 128);
                }
                return t;
            });
            ForEach<ToolColor>(this, (t) =>
            {
                if (t == null)
                {
                    return new ToolColor(128, 128, 128);
                }
                return t;
            });
        }

        private delegate T Change<T>(T t);
        private static void ForEach<T>(object obj, Change<T> action)
        {
            foreach (PropertyInfo propertyInfo in obj.GetType().GetProperties())
            {
                if (propertyInfo.GetAccessors(true)[0].IsStatic)
                    continue;

                if (propertyInfo.SetMethod == null)
                    continue;

                object value = propertyInfo.GetValue(obj, null);
                

                if (typeof(T).IsAssignableFrom(propertyInfo.PropertyType))
                {
                    T tc = (T)value;
                    T newT = action(tc);
                    propertyInfo.SetValue(obj, newT, null);
                }
                else if (value != null)
                {
                    ForEach<T>(value, action);
                }
            }
        }

        private static void AutoName(string themeName, string parent, object obj)
        {
            foreach (PropertyInfo propertyInfo in obj.GetType().GetProperties())
            {
                object value = propertyInfo.GetValue(obj, null);
                if (value == null)
                    continue;

                if (propertyInfo.PropertyType == typeof(ToolTexture))
                {
                    ToolTexture tt = (ToolTexture)value;
                    if (string.IsNullOrEmpty(tt.TextureFilename))
                    {

                        if (string.IsNullOrEmpty(parent))
                        {
                            tt.TextureFilename = propertyInfo.Name + ".png";
                        }
                        else
                        {
                            tt.TextureFilename = parent + "." + propertyInfo.Name + ".png";
                        }
                    }
                }

                if (propertyInfo.PropertyType == typeof(PilotTheme) || typeof(PanelTheme).IsAssignableFrom(propertyInfo.PropertyType))
                {
                    AutoName(themeName, propertyInfo.Name, value);
                }
            }
        }

        public static void LocaliseFilenames(IEnumerable<Theme> themes)
        {
            foreach (Theme theme in themes)
            {
                if (theme != Current)
                {
                    LocaliseFilenames(theme.Directory, theme);
                }
            }
        }

        private static void LocaliseFilenames(DirectoryInfo target, object obj)
        {
            foreach (PropertyInfo propertyInfo in obj.GetType().GetProperties())
            {
                object value = propertyInfo.GetValue(obj, null);
                if (value == null)
                    continue;

                if (propertyInfo.PropertyType == typeof(ToolTexture))
                {
                    ToolTexture tt = (ToolTexture)value;

                    string filename = tt.TextureFilename.Replace('\\','/');

                    if (!string.IsNullOrEmpty(filename) && !File.Exists(filename))
                    {
                        tt.TextureFilename = Path.Combine(target.FullName, filename);
                    }
                    else
                    {
                        tt.TextureFilename = filename;
                    }
                }

                if (propertyInfo.PropertyType == typeof(PilotTheme) || typeof(PanelTheme).IsAssignableFrom(propertyInfo.PropertyType))
                {
                    LocaliseFilenames(target, value);
                }
            }
        }

        public void CopyImageToTheme(string source, string localdestination)
        {
            CheckCreateDirectory();

            string name = Directory.FullName + localdestination;

            if (!File.Exists(name) && File.Exists(source))
            {
                File.Copy(source, name);
            }
        }

        private void CheckCreateDirectory()
        {
            if (!Directory.Exists)
            {
                Directory.Create();
            }
        }

        public override string ToString()
        {
            if (Name == "Dark")
                return "Dark (Default)";

            return Name;
        }
    }

    public class PanelTheme
    {
        public ToolTexture Background { get; set; }
        public ToolTexture Foreground { get; set; }
        public ToolColor Text { get; set; }

        public PanelTheme()
        {
            Background = new ToolTexture(22, 22, 22);
            Foreground = new ToolTexture(27, 27, 27);
            Text = new ToolColor(255, 255, 255);
        }
    }

    public class BorderPanelTheme : PanelTheme
    {
        public ToolColor Border { get; set; }
        public BorderPanelTheme()
        {
            Border = new ToolColor(15, 15, 15);
        }
    }

    public class PilotListPanelTheme : PanelTheme
    {
        public ToolTexture Channel { get; set; }
        public ToolColor PilotCount { get; set; }

        public PilotListPanelTheme()
        {
            Channel = new ToolTexture(255, 255, 255);
            PilotCount = new ToolColor(255, 255, 255);
        }
    }

    public class RoundPanelTheme : BorderPanelTheme
    {
        public ToolTexture RaceTitle { get; set; }
        public ToolTexture Channel { get; set; }
        public ToolTexture Heading { get; set; }

        public RoundPanelTheme()
        {
            Channel = new ToolTexture(255, 255, 255);
            Heading = new ToolTexture(27, 27, 27);
            RaceTitle = new ToolTexture(27, 27, 27);
        }
    }

    public class InfoPanelTheme : BorderPanelTheme
    {
        public ToolTexture Heading { get; set; }
        public ToolColor HeadingText { get; set; }

        public InfoPanelTheme()
        {
            Heading = new ToolTexture(27, 27, 27);
            HeadingText = new ToolColor(255, 255, 255);
        }
    }

    public class PilotTheme
    {
        public ToolTexture LapBackground { get; set; }
        public ToolColor PositionText { get; set; }
        public ToolColor PilotOverlayText { get; set; }
        public ToolTexture PilotOverlayPanel { get; set; }
        public int PilotTitleAlpha { get; set; }

        public ToolTexture PilotNameBackground { get; set; }
        public ToolTexture PBBackground { get; set; }

        public ToolTexture NoVideoBackground { get; set; }
        public ToolTexture CrashedOut { get; set; }

        public PilotTheme()
        {
            LapBackground = new ToolTexture(28, 28, 28, 180);
            PositionText = new ToolColor(255, 255, 255);
            PilotOverlayText = new ToolColor(255, 255, 255);
            PilotOverlayPanel = new ToolTexture(28, 28, 28, 180);
            PilotTitleAlpha = 160;

            PilotNameBackground = new ToolTexture("pilot.png", 28, 28, 28, 180);
            PBBackground = new ToolTexture("pbbg.png", 28, 28, 28, 180);


            NoVideoBackground = new ToolTexture("static.png", 22, 22, 22, 255);
            CrashedOut = new ToolTexture("crashed.png", 22, 22, 22, 255);
        }
    }

    public class TrackTheme : InfoPanelTheme
    {
        public ToolColor GateLabel { get; set; }

        public TrackTheme()
        {
            GateLabel = new ToolColor(255, 0, 0);
            Heading = new ToolTexture(236, 28, 35, 200);
        }

    }

}
