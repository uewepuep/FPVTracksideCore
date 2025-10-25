using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Tools
{
    public static class IOTools
    {
        public static DirectoryInfo WorkingDirectory { get; set; }

        /// <summary>
        /// Optional: EventStorageLocation setting for macOS absolute path detection.
        /// Set this from UI layer to enable custom base directory on macOS.
        /// </summary>
        public static string EventStorageLocation { get; set; }

        /// <summary>
        /// Gets the base directory for user data.
        /// On macOS: If EventStorageLocation is an absolute path, uses it as base directory.
        ///           If path ends with /events/, strips that off since it's just the subfolder.
        ///           Otherwise uses WorkingDirectory (Application Support).
        /// On Windows: Always uses WorkingDirectory (current directory).
        /// </summary>
        public static DirectoryInfo GetBaseDirectory()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                // On macOS, check if EventStorageLocation is set to an absolute path
                if (!string.IsNullOrEmpty(EventStorageLocation) && Path.IsPathRooted(EventStorageLocation))
                {
                    // Absolute path - use it as base directory
                    DirectoryInfo dir = new DirectoryInfo(EventStorageLocation);

                    // If user manually added "/events/" to the end, strip it off
                    // since on macOS this setting controls the base directory for ALL data
                    if (dir.Name.ToLower() == "events" && dir.Parent != null)
                    {
                        return dir.Parent;
                    }

                    return dir;
                }

                // On macOS, if WorkingDirectory not set yet, use Application Support directly
                if (WorkingDirectory == null)
                {
                    string appSupport = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
                    return new DirectoryInfo(System.IO.Path.Combine(appSupport, "FPVTrackside"));
                }
            }

            return WorkingDirectory;
        }

        public static T[] Read<T>(Profile profile, string filename) where T : new()
        {
            return Read<T>(profile.GetPath(), filename, null);
        }

        public static T[] Read<T>(string directory, string filename) where T : new()
        {
            return Read<T>(directory, filename, null);
        }

        public static T[] Read<T>(string directory, string filename, IEnumerable<KeyValuePair<string, string>> replacements) where T : new()
        {
            // Use GetBaseDirectory() to respect custom EventStorageLocation on macOS
            FileInfo file = new FileInfo(Path.Combine(GetBaseDirectory().FullName, directory, filename));

            bool deleteAfterReading = false;

            if (!file.Exists)
            {
                return (T[])Activator.CreateInstance(typeof(T[]), 0);
            }

            T[] os = null;

            string contents = null;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    contents = File.ReadAllText(file.FullName);
                    if (replacements != null)
                    {
                        foreach (var kvp in replacements)
                        {
                            contents = contents.Replace(kvp.Key, kvp.Value);
                        }
                    }
                    break;
                }
                catch (Exception e)
                {
                    Logger.Input.LogException("IOToolsRead", e);
                    Thread.Sleep(1000);
                }
            }

            if (contents == null)
            {
                throw new FormatException();
            }

            if (file.Extension.ToLower() == ".json")
            {

                JsonSerializerSettings settings = new JsonSerializerSettings();
                settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                settings.Formatting = Formatting.Indented;

                os = JsonConvert.DeserializeObject<T[]>(contents, settings);
            }

            if (file.Extension.ToLower() == ".xml")
            {
                XmlSerializer serializer = new XmlSerializer(typeof(T[]));
                using (TextReader reader = new StringReader(contents))
                {
                    os = (T[])serializer.Deserialize(reader);
                }
            }

            if (os != null)
            {
                if (deleteAfterReading)
                {
                    file.Delete();
                }
                return os;
            }

            throw new FormatException();
        }

        public static T ReadSingle<T>(string directory, string filename) where T : new()
        {
            return Read<T>(directory, filename).FirstOrDefault();
        }

        public static void Write<T>(Profile profile, string filename, params T[] items) where T : new()
        {
            Write(profile.GetPath(), filename, items);
        }

        public static void Write<T>(string directory, string filename, params T[] items) where T : new()
        {
            // Use GetBaseDirectory() to respect custom EventStorageLocation on macOS
            FileInfo file = new FileInfo(Path.Combine(GetBaseDirectory().FullName, directory, filename));

            if (!file.Directory.Exists)
            {
                file.Directory.Create();
            }

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (file.Extension.ToLower() == ".json")
                    {
                        JsonSerializerSettings settings = new JsonSerializerSettings();
                        settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                        settings.Formatting = Formatting.Indented;

                        string contents = JsonConvert.SerializeObject(items, settings);
                        File.WriteAllText(file.FullName, contents);
                    }

                    if (file.Extension.ToLower() == ".xml")
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(T[]));
                        using (TextWriter writer = new StreamWriter(file.FullName))
                        {
                            serializer.Serialize(writer, items);
                        }
                    }
                    break;
                }
                catch (Exception e)
                {
                    Logger.Input.LogException("IOToolsWrite", e);
                    Thread.Sleep(1000);
                }
            }
        }

        public enum Overwrite
        {
            Never,
            Always,
            IfNewer
        }

        public static void CopyDirectory(DirectoryInfo source, DirectoryInfo dest, Overwrite overwrite)
        {
            // Cache directories before we start copying
            DirectoryInfo[] dirs = source.GetDirectories();

            // Create the destination directory
            if (!dest.Exists)
                dest.Create();

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in source.GetFiles())
            {
                string targetFilePath = Path.Combine(dest.FullName, file.Name);

                bool copy = false;
                bool delete = false;

                switch (overwrite)
                {
                    case Overwrite.Never:
                        copy = !File.Exists(targetFilePath);
                        break;

                    case Overwrite.Always:
                        copy = true;
                        delete = true;
                        break;

                    case Overwrite.IfNewer:
                        FileInfo newFile = new FileInfo(targetFilePath);

                        if (newFile.Exists)
                        {
                            delete = copy = newFile.LastWriteTime > file.LastWriteTime;
                        }
                        else
                        {
                            copy = true;
                        }

                        break;
                }

                if (delete && File.Exists(targetFilePath))
                {
                    File.Delete(targetFilePath);
                }

                if (copy)
                {
                    file.CopyTo(targetFilePath);
                }
            }

            foreach (DirectoryInfo subDir in source.GetDirectories())
            {
                string newDestinationDir = Path.Combine(dest.FullName, subDir.Name);
                CopyDirectory(subDir, new DirectoryInfo(newDestinationDir), overwrite);
            }
        }
    }
}
