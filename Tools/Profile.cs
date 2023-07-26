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
            DirectoryInfo dataInfo = new DirectoryInfo(Path.Combine(workingDirectory.FullName, dataDir));

            foreach (DirectoryInfo directoryInfo in dataInfo.EnumerateDirectories())
            {
                yield return new Profile(directoryInfo.Name);
            }
        }

        public static Profile AddProfile(DirectoryInfo workingDirectory, string name)
        {
            DirectoryInfo dataInfo = new DirectoryInfo(Path.Combine(workingDirectory.FullName, dataDir));

            try
            {
                dataInfo.CreateSubdirectory(name);
                return new Profile(name);
            }

            catch 
            {
                return null;
            }
        }
    }
}
