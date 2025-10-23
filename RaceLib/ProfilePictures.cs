using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public class ProfilePictures
    {
        private string[] extensions = new string[] { ".mp4", ".wmv", ".mkv", ".png", ".jpg" };

        private Guid EventId;

        public ProfilePictures(Guid eventId)
        {
            EventId = eventId;
        }

        public IEnumerable<FileInfo> GetPilotProfileMedia()
        {
            // On macOS: uses Application Support, or custom absolute path if EventStorageLocation is absolute
            // On Windows: uses current directory
            DirectoryInfo pilotProfileDirectory = new DirectoryInfo(Path.Combine(IOTools.GetBaseDirectory().FullName, "pilots"));

            if (!pilotProfileDirectory.Exists) {
                pilotProfileDirectory.Create();
            }

            if (pilotProfileDirectory.Exists)
            {
                foreach (FileInfo file in pilotProfileDirectory.GetFiles())
                {
                    if (extensions.Contains(file.Extension))
                    {
                        yield return file;
                    }
                }
            }
        }

        public void FindProfilePicture(Pilot pilot)
        {
            FindProfilePictures(new[] { pilot });
        }

        public void FindProfilePictures(Pilot[] pilots)
        {
            // On macOS: uses Application Support, or custom absolute path if EventStorageLocation is absolute
            // On Windows: uses current directory
            string currentDirectory = IOTools.GetBaseDirectory().FullName;

            List<string> listOfExt = extensions.ToList();

            Patreon[] patreonWithHandle = null;
            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                patreonWithHandle = db.All<Patreon>().Where(pa => !string.IsNullOrEmpty(pa.Handle)).ToArray();
            }

            FileInfo[] media = GetPilotProfileMedia().ToArray();
            foreach (Pilot p in pilots)
            {
                if (p != null)
                {
                    try
                    {
                        string oldPath = p.PhotoPath;
                        if (string.IsNullOrEmpty(p.PhotoPath))
                        {
                            IEnumerable<FileInfo> matches = media.Where(f => f.Name.ToLower().Contains(p.Name.ToLower()));
                            if (matches.Any())
                            {
                                p.PhotoPath = matches.OrderByDescending(f => listOfExt.IndexOf(f.Extension)).FirstOrDefault().FullName;
                            }
                        }
                        if (!string.IsNullOrEmpty(p.PhotoPath))
                        {
                            p.PhotoPath = Path.GetRelativePath(currentDirectory, p.PhotoPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.UI.LogException(this, ex);
                    }
                    
                    try
                    {
                        if (p.PhotoPath == null)
                        {
                            Patreon match = patreonWithHandle.FirstOrDefault(pa => pa.Handle.ToLower() == p.Name.ToLower());
                            if (match != null)
                            {
                                p.PhotoPath = match.ThumbFilename;
                            }
                        }
                        
                    }
                    catch (Exception ex)
                    {
                        Logger.UI.LogException(this, ex);
                    }
                    
                }
            }

            using (IDatabase db = DatabaseFactory.Open(EventId))
            {
                db.Update(pilots);
            }
        }
    }
}
