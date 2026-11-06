using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Brings the saved living bots back from their saved profiles, where they stood, through the game's own path for
    /// a ready profile: BotCreatorClient.ActivateBot(Profile, PositionNote, BotZone, ...) → LocalGame.CreateBotByProfile
    /// → BotOwner.Create → PreActivate (where SAIN attaches). The callback registers each bot with the spawner exactly
    /// as a normal spawn does (BotSpawner.ActivateBotCallback: bot list, counts, OnBotCreated, death callback).
    ///
    /// Groups: scavs go through BotSpawner.GetGroupAndSetEnemies, which keeps one group per zone, side and role, as
    /// they had. Bosses, followers and PMCs get one BotsGroup per saved group (as the game does for bosses and ABPS
    /// for PMC squads), so squads stay friends; a saved boss calls SetBoss and its followers rejoin it.
    ///
    /// Creation is asynchronous (the player object loads); the bots appear over the next frames, while the raid is
    /// still frozen.
    /// </summary>
    internal static class BotRestore
    {
        internal static string Spawn(IBotGame game, RaidState state)
        {
            var bots = state.Bots ?? new List<BotState>();
            if (bots.Count == 0)
            {
                return "no living bots saved";
            }
            var controller = game?.BotsController;
            var spawner = controller?.BotSpawner;
            if (!(spawner?._botCreator is BotCreatorClient creator))
            {
                return "bots NOT restored: no bot spawner";
            }

            var sizes = bots.GroupBy(b => b.Group).ToDictionary(g => g.Key, g => g.Count());
            var groups = new Dictionary<int, BotsGroup>();
            int started = 0, missing = 0, failed = 0;
            // Bosses first, as the game spawns them before their followers.
            foreach (var saved in bots.OrderByDescending(b => b.Boss))
            {
                if (!ResumeSession.BotProfiles.TryGetValue(saved.ProfileId, out var profile))
                {
                    missing++;
                    continue;
                }
                try
                {
                    var position = Vec3(saved.Position);
                    var zone = (string.IsNullOrEmpty(saved.Zone) ? null : spawner.GetZoneByName(saved.Zone))
                               ?? spawner.GetClosestZone(position, out _);
                    var corePoint = controller.CoversData.GetClosest(position).CorePointInGame.Id;
                    var current = saved;
                    spawner._inSpawnProcess++;
                    var task = creator.ActivateBot(profile, new PositionNote(position, corePoint, isUsed: true), zone, shallBeGroup: false,
                        (bot, botZone) => GroupFor(spawner, bot, botZone, current, groups, sizes),
                        bot => Activated(spawner, bot, current),
                        spawner.GetCancelToken());
                    Watch(task, saved.ProfileId);
                    started++;
                }
                catch (Exception e)
                {
                    failed++;
                    Plugin.Log.LogError($"Restoring bot {saved.ProfileId} ({saved.Role}) failed: {e}");
                }
            }
            return $"{started}/{bots.Count} bots spawning" + (missing > 0 ? $", {missing} without a profile" : "") + (failed > 0 ? $", {failed} FAILED" : "");
        }

        private static BotsGroup GroupFor(BotSpawner spawner, BotOwner bot, BotZone zone, BotState saved,
            Dictionary<int, BotsGroup> groups, Dictionary<int, int> sizes)
        {
            // Normal spawns have this before PreActivate; parts of the game read it.
            bot.SpawnProfileData = ProfileData(bot.Profile);
            var role = bot.Profile.Info.Settings.Role;
            var side = bot.Profile.Info.Side;
            var bossGroup = role.IsBoss() || role.IsFollower() || side == EPlayerSide.Usec || side == EPlayerSide.Bear;
            if (!bossGroup || saved.Group == 0)
            {
                return spawner.GetGroupAndSetEnemies(bot, zone);
            }
            if (groups.TryGetValue(saved.Group, out var group))
            {
                spawner.SetBotAsEnemy(bot);
                return group;
            }
            var enemies = spawner.GetBotEnemiesList(bot).ToList();
            spawner.SetBotAsEnemy(bot);
            group = new BotsGroup(zone, spawner.BotGame, bot, enemies, spawner._deadBodiesController, spawner._allPlayers, forBoss: true);
            // The saved target, not the survivors: ORBIT sizes its squad from it, and the game's IsFull follows it.
            group.TargetMembersCount = saved.GroupTarget > 0 ? saved.GroupTarget : sizes.TryGetValue(saved.Group, out var size) ? size : 1;
            spawner.Groups.Add(zone, side, group, isBossOrFollower: true);
            groups.Add(saved.Group, group);
            return group;
        }

        private static void Activated(BotSpawner spawner, BotOwner bot, BotState saved)
        {
            spawner.ActivateBotCallback(bot, new BotCreationData(ProfileData(bot.Profile)), null, false, new Stopwatch());
            var player = bot.GetPlayer;
            var notes = new List<string>();
            try
            {
                if (saved.Rotation != null && saved.Rotation.Length == 2)
                {
                    player.Rotation = new Vector2(saved.Rotation[0], saved.Rotation[1]);
                }
                player.MovementContext.SetPoseLevel(saved.PoseLevel, force: true);
                notes.Add(RaidRestore.RestoreEffects(player.ActiveHealthController, saved.Effects));
                if (saved.Boss)
                {
                    bot.Boss.SetBoss(saved.FollowersTarget);
                    notes.Add($"boss of {saved.FollowersTarget}");
                }
                if ((player.Side == EPlayerSide.Usec || player.Side == EPlayerSide.Bear) && AbpsCompat.AddPmc(player))
                {
                    notes.Add("known to ABPS");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Restoring bot {saved.ProfileId} after spawn failed partway: {e}");
            }
            var health = player.HealthController;
            Plugin.Log.LogInfo($"Bot back: {player.Profile.Nickname} {saved.ProfileId} ({saved.Role}, group {saved.Group}) at {player.Position} " +
                               $"(saved {Vec3(saved.Position)}), hp {health?.GetBodyPartHealth(EBodyPart.Common).Current:F0}, " +
                               // SAIN hasn't activated the bot yet here; "SAIN personality kept" follows when it does.
                               $"saved SAIN personality {saved.Personality ?? "-"}" +
                               (notes.Count > 0 ? "; " + string.Join(", ", notes.Where(n => !string.IsNullOrEmpty(n))) : ""));
        }

        private static GetProfileDataParams ProfileData(Profile profile)
        {
            var settings = profile.Info.Settings;
            return new GetProfileDataParams(profile.Info.Side, settings.Role, settings.BotDifficulty, 0f);
        }

        private static void Watch(Task task, string profileId)
        {
            task.ContinueWith(t => Plugin.Log.LogError($"Restoring bot {profileId} failed: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
        }

        private static Vector3 Vec3(float[] v)
        {
            return v != null && v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.zero;
        }
    }
}
