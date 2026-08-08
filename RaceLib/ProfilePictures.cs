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
            DirectoryInfo pilotProfileDirectory = new DirectoryInfo(IOTools.ResolveFromWorkingDirectory("pilots"));

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
            string workingDirectory = IOTools.WorkingDirectory.FullName;

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
                            // macOS stores file names decomposed (NFD), so both sides are
                            // brought to NFC before comparing. The matched FileInfo keeps
                            // the on-disk name for the actual file access.
                            string pilotName = p.Name.NormaliseUnicode().ToLower();
                            IEnumerable<FileInfo> matches = media.Where(f => f.Name.NormaliseUnicode().ToLower().Contains(pilotName));
                            if (matches.Any())
                            {
                                p.PhotoPath = matches.OrderByDescending(f => listOfExt.IndexOf(f.Extension)).FirstOrDefault().FullName;
                            }
                        }
                        if (!string.IsNullOrEmpty(p.PhotoPath))
                        {
                            p.PhotoPath = Path.GetRelativePath(workingDirectory, IOTools.ResolveFromWorkingDirectory(p.PhotoPath));
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
                            Patreon match = patreonWithHandle.FirstOrDefault(pa => pa.Handle.NormaliseUnicode().ToLower() == p.Name.NormaliseUnicode().ToLower());
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
