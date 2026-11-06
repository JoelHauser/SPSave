using System;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace SPSave.Game
{
    /// <summary>
    /// Holds a restored raid still until the player takes control, the way the Pause mod (netVnum.Pause, built for
    /// 4.1) does: the main player is disabled with its effects paused, players don't tick, the world only ticks
    /// players, and the raid's own update and timer-end check are skipped. Time.timeScale = 0 alone (the first B1
    /// build) left player updates running with a zero frame time, which broke the camera and collisions.
    ///
    /// The patches go on when the freeze starts and come off when it ends, through a Harmony instance of their own:
    /// nothing runs per frame outside a freeze.
    /// </summary>
    internal static class Freeze
    {
        private const string HarmonyId = "com.joelhauser.spsave.freeze";
        private static Harmony _harmony;
        private static Player _player;

        internal static bool Active { get; private set; }

        internal static void Begin(Player player)
        {
            if (Active)
            {
                return;
            }
            _harmony = new Harmony(HarmonyId);
            var skip = new HarmonyMethod(typeof(Freeze), nameof(Skip));
            _harmony.Patch(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.DoWorldTick)),
                prefix: new HarmonyMethod(typeof(Freeze), nameof(WorldTick)));
            _harmony.Patch(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.DoOtherWorldTick)), prefix: skip);
            _harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.UpdateTick)), prefix: skip);
            _harmony.Patch(AccessTools.Method(typeof(EndByTimerScenario), nameof(EndByTimerScenario.Update)), prefix: skip);
            _harmony.Patch(AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), nameof(BaseLocalGame<EftGamePlayerOwner>.Update)), prefix: skip);
            Active = true;

            _player = player;
            if (_player != null)
            {
                _player.enabled = false;
                _player.PauseAllEffectsOnPlayer();
            }
        }

        internal static void End()
        {
            if (!Active)
            {
                return;
            }
            Active = false;
            try
            {
                if (_player != null)
                {
                    _player.enabled = true;
                    _player.UnpauseAllEffectsOnPlayer();
                }
            }
            finally
            {
                _player = null;
                _harmony?.UnpatchSelf();
                _harmony = null;
            }
        }

        private static bool Skip() => !Active;

        private static bool WorldTick(GameWorld __instance, float dt)
        {
            if (!Active)
            {
                return true;
            }
            __instance.PlayerTick(dt);
            return false;
        }
    }
}
