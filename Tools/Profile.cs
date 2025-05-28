using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public class Profile
    {
        private const string dataDir = "data";

        public string Name { get; set; }

        public Profile(string name)
        {
            Name = name;
        }

        public string GetPath()
        {
            return Path.Combine(dataDir, Name);
        }

        public override string ToString()
        {
            return GetPath();
        }

        public static IEnumerable<Profile> GetProfiles(DirectoryInfo workingDirectory) 
        { 
            DirectoryInfo profilesRoot = new DirectoryInfo(Path.Combine(workingDirectory.FullName, dataDir));

            foreach (DirectoryInfo directoryInfo in profilesRoot.EnumerateDirectories())
            {
                yield return new Profile(directoryInfo.Name);
            }
        }

        public static Profile AddProfile(DirectoryInfo workingDirectory, string name)
        {
            DirectoryInfo profilesRoot = new DirectoryInfo(Path.Combine(workingDirectory.FullName, dataDir));

            try
            {
                profilesRoot.CreateSubdirectory(name);
                return new Profile(name);
            }

            catch 
            {
                return null;
            }
        }

        public static bool RenameProfile(DirectoryInfo workingDirectory, Profile profile, string name)
        {
            DirectoryInfo profilesRoot = new DirectoryInfo(Path.Combine(workingDirectory.FullName, dataDir));

            DirectoryInfo profileDir = new DirectoryInfo(Path.Combine(profilesRoot.FullName, profile.Name));
            try
            {
                if (!profileDir.Exists)
                    return false;

                Directory.Move(profileDir.FullName, Path.Combine(profilesRoot.FullName, name));

                profile.Name = name;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool CloneProfile(DirectoryInfo workingDirectory, Profile profile, string name)
        {
            DirectoryInfo profilesRoot = new DirectoryInfo(Path.Combine(workingDirectory.FullName, dataDir));

            DirectoryInfo profileDir = new DirectoryInfo(Path.Combine(profilesRoot.FullName, profile.Name));
            try
            {
                if (!profileDir.Exists)
                    return false;

                string path = Path.Combine(profilesRoot.FullName, name);

                DirectoryInfo newDir = new DirectoryInfo(path);
                newDir.Create();

                foreach (FileInfo fi in profileDir.GetFiles())
                {
                    string target = Path.Combine(newDir.FullName, fi.Name);
                    fi.CopyTo(target);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

    }
}
