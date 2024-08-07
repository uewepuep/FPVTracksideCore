using Composition.Layers;
using Composition.Nodes;
using Composition.Text;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition
{
    public enum PlatformFeature
    {
        Speech,
        Video,
        Windows,
        GMFBridge
    }

    public abstract class PlatformTools
    {
        public abstract ITextRenderer CreateTextRenderer();

        public abstract ISpeaker CreateSpeaker(string voice);
        public IEnumerable<string> GetSpeakerVoices()
        {
            using (ISpeaker speaker = CreateSpeaker(null))
            {
                return speaker.GetVoices();
            }
        }

        public abstract DirectoryInfo WorkingDirectory { get; }

        public abstract IClipboard Clipboard { get; }

        public abstract bool Focused { get; }
        public abstract bool ThreadedDrawing { get; }

        public abstract void ShowNewWindow(Node node);

        public abstract string OpenFileDialog(string title, string fileExtension);
        public abstract string SaveFileDialog(string title, string fileExtension);
        public abstract void Invoke(Action value);

        public abstract void OpenFileManager(string directory);

        public abstract PlatformFeature[] Features { get; }
        public abstract string InstallerExtension { get; }

        public bool HasFeature(PlatformFeature platformFeature)
        {
            return Features.Contains(platformFeature);
        }
        public bool ExportCSV(string title, string csv, PopupLayer popupLayer)
        {
            try
            {
                string filename = SaveFileDialog(title, "CSV|*.csv");
                if (filename != null)
                {
                    File.WriteAllText(filename, csv);
                    return true;
                }
            }
            catch (Exception e)
            {
                popupLayer.PopupMessage(e.Message);
                Tools.Logger.UI.LogException(null, e);
            }
            return false;
        }

        private string loginFile = @"data\login";
        private string key = @"P2l!OOHM@&#lWXGAjsaTf38e^*!GFrHZ";

        public LoginDetails GetSavedUsernamePassword()
        {
            if (File.Exists(loginFile))
            {
                string enc = File.ReadAllText(loginFile);
                string json = Encryptor.DecryptString(key, enc);

                return JsonConvert.DeserializeObject<LoginDetails>(json);
            }
            return new LoginDetails();
        }

        public void SetSavedUsernamePassword(LoginDetails loginDetails)
        {
            string json = JsonConvert.SerializeObject(loginDetails);
            string enc = Encryptor.EncryptString(key, json);
            File.WriteAllText(loginFile, enc);
        }

        public virtual bool Check(string toCheck)
        {
            return false;
        }
    }

    public class LoginDetails
    {
        public string AuthKey { get; set; }

        public string MultiGPKey { get; set; }

        public LoginDetails()
        {
            AuthKey = "";
            MultiGPKey = "";
        }
    }
}
