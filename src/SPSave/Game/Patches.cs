using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Weather;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SPSave.Game
{
    /// <summary>Attaches the capture controller once per raid. Not the hideout: it has a GameWorld but no IBotGame.</summary>
    internal sealed class RaidStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));

        [PatchPostfix]
        private static void Postfix(GameWorld __instance)
        {
            if (!Singleton<IBotGame>.Instantiated)
            {
                return;
            }
            CaptureController.Attach(__instance);
        }
    }

    /// <summary>
    /// Keeps the weather nodes the raid runs on. WeatherController builds its curve from them and does not keep the
    /// array, so this is the one piece of state recorded during play: a reference copy, once or twice per raid
    /// (BaseLocalGame.InitWeather, then LocalGame.vmethod_5 with the backend's weather). Nothing runs per frame.
    /// </summary>
    internal sealed class WeatherNodesPatch : ModulePatch
    {
        internal static WeatherNode[] Last;

        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(WeatherController), nameof(WeatherController.SetWeatherNodes));

        [PatchPostfix]
        private static void Postfix(WeatherNode[] nodes)
        {
            Last = nodes;
        }
    }
}
