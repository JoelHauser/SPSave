using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace SPSave.Server
{
    public record LockRequest : IRequestData
    {
        [JsonPropertyName("map")] public string Map { get; set; }
        [JsonPropertyName("side")] public string Side { get; set; }
        [JsonPropertyName("serverId")] public string ServerId { get; set; }
        /// <summary>Raid Review's recording time at the save; null without Raid Review (or from an older client).</summary>
        [JsonPropertyName("raidReviewElapsedMs")] public long? RaidReviewElapsedMs { get; set; }
    }

    /// <summary>
    /// The client's calls (SPT.Common.Http.RequestHandler sends the session, which is the profile id):
    /// /spsave/lock on Save &amp; Quit, before the game closes; /spsave/unlock when a save is used up or discarded;
    /// /spsave/status at the main menu, to match the save on disk with the lock.
    /// Answers are plain JSON, e.g. {"ok":true} or {"locked":true,"map":...}.
    /// </summary>
    [Injectable(InjectionType.Transient, int.MaxValue, TypePriority = 400001)]
    public class LockRouter : StaticRouter
    {
        public LockRouter(JsonUtil jsonUtil) : base(jsonUtil, Routes())
        {
        }

        private static List<RouteAction> Routes()
        {
            return new List<RouteAction>
            {
                new RouteAction<LockRequest>("/spsave/lock", (url, info, sessionId, output, token) =>
                {
                    var raidLock = new RaidLock
                    {
                        ProfileId = sessionId.ToString(),
                        Map = info.Map,
                        Side = info.Side,
                        ServerId = info.ServerId,
                        LockedUtc = DateTime.UtcNow.ToString("o"),
                        RaidReviewRaidId = RaidReviewContinuation.Hold(sessionId.ToString()),
                        RaidReviewElapsedMs = info.RaidReviewElapsedMs
                    };
                    LockStore.Set(raidLock);
                    SPSaveLoader.Log($"Profile {raidLock.ProfileId} locked: raid on {raidLock.Map} saved ({raidLock.ServerId})" +
                                     (raidLock.RaidReviewRaidId != null ? $"; Raid Review raid {raidLock.RaidReviewRaidId} kept for the resume." : "."));
                    return new ValueTask<string>(Ok());
                }),
                new RouteAction("/spsave/unlock", (url, info, sessionId, output, token) =>
                {
                    if (LockStore.Clear(sessionId.ToString()))
                    {
                        SPSaveLoader.Log($"Profile {sessionId} unlocked: its saved raid is used up or discarded.");
                    }
                    return new ValueTask<object>(Ok());
                }),
                new RouteAction("/spsave/status", (url, info, sessionId, output, token) =>
                {
                    var raidLock = LockStore.Get(sessionId.ToString());
                    var status = new Dictionary<string, object>
                    {
                        ["locked"] = raidLock != null,
                        ["map"] = raidLock?.Map,
                        ["serverId"] = raidLock?.ServerId
                    };
                    return new ValueTask<object>(JsonSerializer.Serialize(status));
                })
            };
        }

        private static string Ok() => "{\"ok\":true}";
    }
}
