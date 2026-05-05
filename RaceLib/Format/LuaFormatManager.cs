using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tools;

namespace RaceLib.Format
{
    public class LuaFormatManager
    {
        private readonly DirectoryInfo directory;
        private readonly Dictionary<string, bool> standingsCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public LuaFormatManager()
            : this(new DirectoryInfo("scripts")) { }

        public LuaFormatManager(DirectoryInfo directory)
        {
            this.directory = directory;
        }

        public bool ScriptHasStandings(string filename)
        {
            if (standingsCache.TryGetValue(filename, out bool has)) return has;
            has = GetScriptFile(filename)?.HasStandings ?? false;
            standingsCache[filename] = has;
            return has;
        }

        public IEnumerable<ScriptFile> GetScriptFiles()
        {
            if (!directory.Exists)
                yield break;

            foreach (FileInfo file in directory.GetFiles("*.lua"))
                yield return new ScriptFile(file);
        }

        public ScriptFile GetScriptFile(string filename)
        {
            return GetScriptFiles().FirstOrDefault(f =>
                string.Equals(f.FileInfo.Name, filename, StringComparison.OrdinalIgnoreCase));
        }

        public class ScriptFile
        {
            public FileInfo FileInfo { get; }
            public string Name { get; private set; }
            public string Description { get; private set; }
            public bool HasStandings { get; private set; }

            public ScriptFile(FileInfo fileInfo)
            {
                FileInfo = fileInfo;
                Name = Path.GetFileNameWithoutExtension(fileInfo.Name);
                Description = string.Empty;

                try
                {
                    Script lua = new Script(CoreModules.Preset_SoftSandbox);
                    lua.DoFile(fileInfo.FullName);
                    DynValue nameDyn = lua.Globals.Get("name");
                    DynValue descDyn = lua.Globals.Get("description");
                    DynValue standingsDyn = lua.Globals.Get("standings");
                    if (nameDyn.Type == DataType.String) Name = nameDyn.String;
                    if (descDyn.Type == DataType.String) Description = descDyn.String;
                    HasStandings = standingsDyn.Type == DataType.Function;
                }
                catch (Exception ex)
                {
                    Logger.AllLog.LogException(this, ex);
                }
            }
        }
    }
}
