using System.Collections.Generic;
using SemanticVersioning;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace SPSave.Server
{
    /// <summary>The one IModMetadata the server needs (as ABPS declares it).</summary>
    public record ModMetadata : IModMetadata
    {
        public string ModGuid { get; init; } = "com.joelhauser.spsave.server";
        public string Name { get; init; } = "SPSave";
        public string Author { get; init; } = "Joel Hauser";
        public List<string> Contributors { get; init; }
        public Version Version { get; init; } = new Version("0.7.1");
        public Range SptVersion { get; init; } = new Range("~4.1.0");
        public bool HasPrepatcher { get; init; }
        public List<string> Incompatibilities { get; init; }
        public Dictionary<string, Range> ModDependencies { get; init; }
        public string Url { get; init; } = "https://github.com/JoelHauser/SPSave";
        public string License { get; init; } = "All rights reserved";
    }
}
