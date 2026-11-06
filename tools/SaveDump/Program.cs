using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SPSave.Core;

namespace SPSave.SaveDump
{
    /// <summary>
    /// Validates a save and lists its sections. With an output directory, writes every section out: JSON sections as
    /// text, binary ones (profiles, world loot) as .bin. Falls back to .tmp and .prev the way a resume would.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: SaveDump <raid.spsave> [output dir]");
                return 2;
            }

            var problems = new List<string>();
            var sections = SnapshotFile.LoadNewestValid(args[0], out var from, problems);
            foreach (var problem in problems)
            {
                Console.WriteLine("skipped " + problem);
            }
            if (sections == null)
            {
                Console.WriteLine("No valid save found.");
                return 1;
            }

            Console.WriteLine($"Loaded {from}");
            foreach (var section in sections.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {section.Key,-40} {section.Value.Length,12:N0} bytes");
            }
            if (sections.TryGetValue("manifest.json", out var manifest))
            {
                Console.WriteLine();
                Console.WriteLine(Encoding.UTF8.GetString(manifest));
            }

            if (args.Length > 1)
            {
                var output = args[1];
                Directory.CreateDirectory(output);
                foreach (var section in sections)
                {
                    var name = section.Key.Replace('/', '_');
                    if (!name.EndsWith(".json", StringComparison.Ordinal))
                    {
                        name += ".bin";
                    }
                    File.WriteAllBytes(Path.Combine(output, name), section.Value);
                }
                Console.WriteLine($"Wrote {sections.Count} sections to {Path.GetFullPath(output)}");
            }
            return 0;
        }
    }
}
