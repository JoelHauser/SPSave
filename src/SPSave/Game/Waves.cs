using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPSave.Core;
using SPT.Reflection.Patching;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Spawn waves across a save. Capture reads the live scenarios; a resume rebuilds the boss scenario from the saved
    /// waves and replaces the old-style waves, both trimmed by Core.WaveTrim, right after LocalGame.Create built them
    /// (its ModifySettings and smethod_8 adjust waves in place, so saved waves must not pass through them again).
    /// The saved waves also stand in for the server's: ABPS regenerates its PMC and scav waves on a server restart.
    /// </summary>
    internal static class Waves
    {
        private static readonly AccessTools.FieldRef<LocalGame, BossSpawnScenario> BossScenario =
            AccessTools.FieldRefAccess<LocalGame, BossSpawnScenario>("_bossSpawnScenario");
        private static readonly AccessTools.FieldRef<LocalGame, WavesSpawnScenario> OldScenario =
            AccessTools.FieldRefAccess<LocalGame, WavesSpawnScenario>("_wavesSpawnScenario");
        private static readonly AccessTools.FieldRef<WavesSpawnScenario, List<TimerManager.ITimer>> OldTimers =
            AccessTools.FieldRefAccess<WavesSpawnScenario, List<TimerManager.ITimer>>("Timers");

        /// <summary>
        /// On a resume, when the raid timer starts (it starts at the saved time played): push the wave timers back by
        /// the time between their scenario's Run and now. Restored waves hold what was left of them counted from the
        /// timer's start, but the scenarios run earlier, during loading, and a resume loads longer than a fresh raid
        /// (0.4.1 test: waves 7 s early). Each wave's Time moves with its timer, so a later save measures from Run as
        /// before. Timers are Time.time-based; TimerManager.ITimer.Restart re-queues them in order.
        /// </summary>
        internal static void AlignToRaidClock()
        {
            if (!(Singleton<AbstractGame>.Instance is LocalGame game))
            {
                return;
            }
            var notes = new List<string>();
            var boss = BossScenario(game);
            if (boss != null && BossRunAt >= 0f)
            {
                var gap = Time.time - BossRunAt;
                foreach (var timer in boss.Timers.ToList())
                {
                    timer.Restart(timer.EndTime + gap);
                }
                foreach (var wave in boss.BossSpawnWaves ?? new BossLocationSpawn[0])
                {
                    if (wave.Time > 0f && !wave.Activated)
                    {
                        wave.Time += gap;
                    }
                }
                notes.Add($"{boss.Timers.Count} boss wave timers +{gap:F1} s");
            }
            var old = OldScenario(game);
            if (old != null && old.Enabled && OldRunAt >= 0f)
            {
                var gap = Time.time - OldRunAt;
                var timers = OldTimers(old) ?? new List<TimerManager.ITimer>();
                foreach (var timer in timers.ToList())
                {
                    timer.Restart(timer.EndTime + gap);
                }
                foreach (var wave in old.SpawnWaves ?? new SpawnWave[0])
                {
                    if (wave.Time >= 0f)
                    {
                        wave.Time += gap;
                    }
                }
                notes.Add($"{timers.Count} old-style wave timers +{gap:F1} s");
            }
            if (notes.Count > 0)
            {
                Plugin.Log.LogInfo("Wave timers aligned to the raid clock (loading time): " + string.Join("; ", notes) + ".");
            }
        }

        /// <summary>
        /// Time.time when each scenario ran: wave timers count from there. Recorded once per raid (WaveClockPatches);
        /// -1 until then.
        /// </summary>
        internal static float BossRunAt = -1f;
        internal static float OldRunAt = -1f;

        /// <summary>AlignToRaidClock ran for this raid (GameTimer.Start may run more than once).</summary>
        internal static bool Aligned;

        internal static WavesState Capture()
        {
            var state = new WavesState { AbpsBotsSpawnedPerPlayer = AbpsCompat.ReadSpawnedPerPlayer() };
            if (!(Singleton<AbstractGame>.Instance is LocalGame game))
            {
                return state;
            }
            var boss = BossScenario(game);
            if (boss?.BossSpawnWaves != null && BossRunAt >= 0f)
            {
                state.BossElapsed = Time.time - BossRunAt;
                state.Boss = boss.BossSpawnWaves.Select(Save).ToList();
            }
            var old = OldScenario(game);
            if (old != null && old.Enabled && old.SpawnWaves != null && OldRunAt >= 0f)
            {
                state.OldElapsed = Time.time - OldRunAt;
                state.Old = old.SpawnWaves.Select(w => new OldWaveState
                {
                    Time = w.Time,
                    BotsCount = w.BotsCount,
                    Difficulty = w.Difficulty,
                    WildSpawnType = w.WildSpawnType,
                    SpawnAreaName = w.SpawnAreaName,
                    Side = w.Side,
                    IsPlayers = w.IsPlayers,
                    ChanceGroup = w.ChanceGroup,
                    KeepZoneOnSpawn = w.KeepZoneOnSpawn
                }).ToList();
            }
            return state;
        }

        private static BossWaveState Save(BossLocationSpawn w)
        {
            return new BossWaveState
            {
                BossName = w.BossName,
                BossChance = w.BossChance,
                BossZone = w.BossZone,
                BossPlayer = w.BossPlayer,
                BossDifficult = w.BossDifficult,
                BossEscortDifficult = w.BossEscortDifficult,
                BossEscortType = w.BossEscortType,
                BossEscortAmount = w.BossEscortAmount,
                Time = w.Time,
                Delay = w.Delay,
                TriggerId = w.TriggerId,
                TriggerName = w.TriggerName,
                IgnoreMaxBots = w.IgnoreMaxBots,
                ForceSpawn = w.ForceSpawn,
                DependKarma = w.DependKarma,
                PerfectPos = w.PerfectPos is Vector3 p ? new[] { p.x, p.y, p.z } : null,
                Supports = w.Supports?.Select(s => new SupportState
                {
                    BossEscortType = s.BossEscortType,
                    BossEscortAmount = s.BossEscortAmount,
                    BossEscortDifficult = s.BossEscortDifficult
                }).ToList(),
                ShowOnTarkovMap = w.ShowOnTarkovMap,
                ShowOnTarkovMapPvE = w.ShowOnTarkovMapPvE,
                ShallSpawn = w.ShallSpawn,
                Activated = w.Activated,
                BornZone = w.BornZone,
                EscortCount = w.EscortCount
            };
        }

        private static BossLocationSpawn Load(BossWaveState s, float time)
        {
            return new BossLocationSpawn
            {
                BossName = s.BossName ?? "",
                BossChance = s.BossChance,
                BossZone = s.BossZone ?? "",
                BossPlayer = s.BossPlayer,
                BossDifficult = s.BossDifficult,
                BossEscortDifficult = s.BossEscortDifficult,
                BossEscortType = s.BossEscortType ?? "",
                BossEscortAmount = s.BossEscortAmount ?? "",
                Time = time,
                Delay = s.Delay,
                TriggerId = s.TriggerId ?? "",
                TriggerName = s.TriggerName ?? "",
                IgnoreMaxBots = s.IgnoreMaxBots,
                ForceSpawn = s.ForceSpawn,
                DependKarma = s.DependKarma,
                PerfectPos = s.PerfectPos != null && s.PerfectPos.Length == 3 ? new Vector3(s.PerfectPos[0], s.PerfectPos[1], s.PerfectPos[2]) : (Vector3?)null,
                Supports = s.Supports?.Select(x => new WildSpawnSupports
                {
                    BossEscortType = x.BossEscortType,
                    BossEscortAmount = x.BossEscortAmount,
                    BossEscortDifficult = x.BossEscortDifficult ?? new string[0]
                }).ToArray(),
                ShowOnTarkovMap = s.ShowOnTarkovMap,
                ShowOnTarkovMapPvE = s.ShowOnTarkovMapPvE
            };
        }

        /// <summary>After LocalGame.Create on a resume: swap in the saved waves. Returns false if the save has none.</summary>
        internal static bool Restore(LocalGame game, WavesState saved)
        {
            if (saved == null)
            {
                Plugin.Log.LogInfo("Waves: none saved (save from before 0.4.0); fresh spawns stay held this raid.");
                return false;
            }
            var notes = new List<string>();

            var boss = BossScenario(game);
            if (boss != null && saved.BossElapsed >= 0f && saved.Boss != null && saved.Boss.All(w => !string.IsNullOrEmpty(w.BossName)))
            {
                var keptStates = new List<BossWaveState>();
                var kept = new List<BossLocationSpawn>();
                foreach (var state in saved.Boss)
                {
                    var time = WaveTrim.Boss(state.Time, !string.IsNullOrEmpty(state.TriggerName), state.ShallSpawn, state.Activated, saved.BossElapsed);
                    if (time == null)
                    {
                        continue;
                    }
                    keptStates.Add(state);
                    kept.Add(Load(state, time.Value));
                }
                var scenario = BossSpawnScenario.Create(kept.ToArray(), boss._spawnBossAction);
                // Create's Init rolled the chance, zone and escort count again: put back what the raid had rolled.
                // Init keeps the array it was given, so the order matches.
                for (var i = 0; i < kept.Count; i++)
                {
                    kept[i].ShallSpawn = true;
                    if (!string.IsNullOrEmpty(keptStates[i].BornZone))
                    {
                        kept[i].BornZone = keptStates[i].BornZone;
                    }
                    kept[i].EscortCount = keptStates[i].EscortCount;
                }
                BossScenario(game) = scenario;
                notes.Add($"{kept.Count}/{saved.Boss.Count} boss waves to come" +
                          (kept.Count > 0 ? $" (next {kept.Where(w => w.Time > 0f).Select(w => (float?)w.Time).Min():F0} s: " +
                                            string.Join(", ", kept.OrderBy(w => w.Time).Take(4).Select(w => $"{w.BossType}@{w.Time:F0}")) + ")" : ""));
            }
            else
            {
                // No usable boss waves in the save: an empty scenario, so none fire again from zero.
                if (boss != null)
                {
                    BossScenario(game) = BossSpawnScenario.Create(Array.Empty<BossLocationSpawn>(), boss._spawnBossAction);
                }
                notes.Add("no boss waves saved: none this raid");
            }

            var old = OldScenario(game);
            if (old != null && old.Enabled)
            {
                var waves = saved.OldElapsed < 0f ? new SpawnWave[0] : saved.Old
                    .Select(w => new { w, time = WaveTrim.Old(w.Time, saved.OldElapsed) })
                    .Where(x => x.time != null)
                    .Select(x => new SpawnWave
                    {
                        Time = x.time.Value,
                        BotsCount = x.w.BotsCount,
                        Difficulty = x.w.Difficulty,
                        WildSpawnType = x.w.WildSpawnType,
                        SpawnAreaName = x.w.SpawnAreaName,
                        Side = x.w.Side,
                        IsPlayers = x.w.IsPlayers,
                        ChanceGroup = x.w.ChanceGroup,
                        KeepZoneOnSpawn = x.w.KeepZoneOnSpawn
                    })
                    .OrderBy(w => w.Time)
                    .ToArray();
                old.SpawnWaves = waves;
                notes.Add($"{waves.Length}/{saved.Old.Count} old-style waves to come");
            }

            Plugin.Log.LogInfo("Waves restored: " + string.Join("; ", notes));
            return true;
        }
    }

    /// <summary>
    /// Records when each wave scenario runs: the one piece of wave state the game doesn't keep (timers count from it).
    /// One float per raid, at raid start; nothing per frame.
    /// </summary>
    internal sealed class BossRunPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BossSpawnScenario), nameof(BossSpawnScenario.Run));

        [PatchPrefix]
        private static void Prefix() => Waves.BossRunAt = Time.time;
    }

    internal sealed class OldWavesRunPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(WavesSpawnScenario), nameof(WavesSpawnScenario.Run));

        /// <summary>LocalGame runs BeforeGameStarted (start waves only, no timers), then AfterGameStarted (the timers).</summary>
        [PatchPrefix]
        private static void Prefix(EBotsSpawnMode spawnMode)
        {
            if (spawnMode != EBotsSpawnMode.BeforeGameStarted)
            {
                Waves.OldRunAt = Time.time;
            }
        }
    }

    /// <summary>A new raid's scenarios exist: forget the last raid's clock, and on a resume swap in the saved waves.</summary>
    internal sealed class LocalGameCreatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LocalGame), nameof(LocalGame.Create));

        [PatchPostfix]
        private static void Postfix(LocalGame __result)
        {
            Waves.BossRunAt = -1f;
            Waves.Aligned = false;
            Waves.OldRunAt = -1f;
            if (!ResumeSession.Loading || __result == null)
            {
                return;
            }
            try
            {
                ResumeSession.WavesRestored = Waves.Restore(__result, ResumeSession.State?.Waves);
                BossNotifierCompat.Rebuild(ResumeSession.State?.Waves);
            }
            catch (Exception e)
            {
                // The scenarios may be half swapped; holding fresh spawns is the safe side.
                ResumeSession.WavesRestored = false;
                Plugin.Log.LogError("Restoring spawn waves failed; fresh spawns stay held this raid. " + e);
            }
        }
    }
}
