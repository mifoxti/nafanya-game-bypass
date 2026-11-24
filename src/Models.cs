using System;
using System.Collections.Generic;

namespace NafanyaGameBypass
{
    public sealed class GameChoice
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public string Pattern { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class SavedState
    {
        public int Version { get; set; }
        public List<GameChoice> Games { get; set; }
        public List<string> ManagedPatterns { get; set; }
        public string LastBackup { get; set; }
        public string LastStateBackup { get; set; }
        public string LastAppliedHash { get; set; }
        public string LastBackupHash { get; set; }
        [System.Web.Script.Serialization.ScriptIgnore]
        public bool NeedsReapply { get; set; }

        public SavedState()
        {
            Version = 1;
            Games = new List<GameChoice>();
            ManagedPatterns = new List<string>();
        }
    }

    public sealed class PreparedChange
    {
        public byte[] Before { get; set; }
        public byte[] After { get; set; }
        public string ConfigHash { get; set; }
        public int RuntimePid { get; set; }
        public List<string> Patterns { get; set; }
        public SavedState PreviousState { get; set; }
        public List<GameChoice> Choices { get; set; }
        public int PreservedRules { get; set; }
    }
}
