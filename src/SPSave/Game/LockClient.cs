using System;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace SPSave.Game
{
    /// <summary>The server's view of a waiting save (SPSave server mod, /spsave/status).</summary>
    internal sealed class LockStatus
    {
        [JsonProperty("locked")] public bool Locked { get; set; }
        [JsonProperty("map")] public string Map { get; set; }
        [JsonProperty("serverId")] public string ServerId { get; set; }
    }

    /// <summary>
    /// Calls to the SPSave server mod. The server keeps the lock; while it exists the profile can't change (stash,
    /// traders, flea, hideout, quests, other raids), so a resumed save can't duplicate or lose gear. The session sent
    /// with each request is the profile id. Every call returns null or false when the server mod is missing.
    /// </summary>
    internal static class LockClient
    {
        internal static bool Lock(Manifest manifest)
        {
            // The raid side as local/start sends it (saves are PMC raids), not manifest.Side (the character's: Usec/Bear).
            var body = JsonConvert.SerializeObject(new { map = manifest.Map, side = "Pmc", serverId = manifest.ServerId });
            return IsOk(Post("/spsave/lock", body));
        }

        internal static bool Unlock()
        {
            return IsOk(Post("/spsave/unlock", "{}"));
        }

        internal static LockStatus Status()
        {
            var text = Post("/spsave/status", "{}");
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<LockStatus>(text);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("SPSave server mod gave an unreadable status (is it installed?): " + e.Message);
                return null;
            }
        }

        private static bool IsOk(string text)
        {
            return text != null && text.Contains("\"ok\":true");
        }

        private static string Post(string path, string body)
        {
            try
            {
                return RequestHandler.PostJson(path, body);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"SPSave server call {path} failed (is the SPSave server mod installed?): {e.Message}");
                return null;
            }
        }
    }
}
