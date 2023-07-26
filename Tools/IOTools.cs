using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Tools
{
    public static class IOTools
    {
        public static DirectoryInfo WorkingDirectory { get; set; }

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
            FileInfo file = new FileInfo(Path.Combine(WorkingDirectory.FullName, directory, filename));

            bool deleteAfterReading = false;

            if (!file.Exists)
            {
                return (T[])Activator.CreateInstance(typeof(T[]), 0);
            }

            T[] os = null;

            string contents = File.ReadAllText(file.FullName);

            if (replacements != null)
            {
                foreach (var kvp in replacements)
                {
                    contents = contents.Replace(kvp.Key, kvp.Value);
                }
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

            //if (file.Extension.ToLower() == ".csv")
            //{
            //    CSVSerializer<T> serializer = new CSVSerializer<T>();
            //    serializer.UseLineNumbers = false;

            //    using (FileStream reader = new FileStream(file.FullName, FileMode.Open))
            //    {
            //       os = serializer.Deserialize(reader).ToArray();
            //    }
            //}

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
            FileInfo file = new FileInfo(Path.Combine(WorkingDirectory.FullName, directory, filename));

            if (!file.Directory.Exists)
            {
                file.Directory.Create();
            }

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

            //if (file.Extension.ToLower() == ".csv")
            //{
            //    CSVSerializer<T> serializer = new CSVSerializer<T>();
            //    serializer.UseLineNumbers = false;

            //    using (FileStream writer = new FileStream(filename, FileMode.Create))
            //    {
            //        serializer.Serialize(writer, items);
            //    }
            //}
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
