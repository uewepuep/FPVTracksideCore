using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI
{
    public class GeneralSettings
    {
        public static GeneralSettings Instance { get; protected set; }
        
        [Category("General")]
        [NeedsRestart]
        public string Profile { get; set; }


        public GeneralSettings()
        {
            Profile = "Profile 1";
        }

        protected const string filename = "GeneralSettings.xml";
        protected const string directory = "data";
        public static GeneralSettings Initialise()
        {
            GeneralSettings generalSettings = null;

            bool error = false;
            try
            {
                GeneralSettings[] s = IOTools.Read<GeneralSettings>(directory, filename);
                
                if (s != null && s.Any())
                {
                    generalSettings = s[0];
                    Write(generalSettings);
                }
                else
                {
                    error = true;
                }
            }
            catch
            {
                error = true;
            }

            if (error)
            {
                GeneralSettings s = new GeneralSettings();
                Write(s);
                generalSettings = s;
            }

            Instance = generalSettings;
            return generalSettings;
        }

        public static void Write(GeneralSettings s)
        {
            IOTools.Write(directory, filename, s);
        }

        public static void Write()
        {
            IOTools.Write(directory, filename, new GeneralSettings[] { Instance });
        }

        public override string ToString()
        {
            return "General Settings";
        }
    }
}
