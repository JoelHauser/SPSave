using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using SAIN.Models.Preset.Personalities;
using SAIN.Preset.Shared.Models.Preset.Personalities;
using SAIN.Preset.Shared.Personalities.BasePersonality;
using SAIN.SAINComponent.Classes.Info;
using SPT.Reflection.Patching;

namespace SPSave.Game
{
    /// <summary>
    /// SAIN (me.sol.sain) is optional. It attaches to every bot at BotOwner.PreActivate, restored ones included, but
    /// draws a new personality for each bot object (PersonalityDictionary.GetPersonality, from SAINBotInfoClass's
    /// constructor and again in ConfigureBot); the SAIN Personality Mixer prefixes the same method for PMCs. A restored
    /// bot keeps its saved personality through a postfix, which runs after both. See docs/B2-COMPAT.md.
    ///
    /// Only SainPersonalityPatch names SAIN types, and it is created only when SAIN is loaded.
    /// </summary>
    internal static class SainCompat
    {
        internal const string Guid = "me.sol.sain";

        /// <summary>Restored bots' personalities by profile id, for the current resume.</summary>
        internal static readonly Dictionary<string, string> Pinned = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Profile ids already logged, so each restored bot's pin is logged once.</summary>
        internal static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);

        private static bool _looked;
        private static MethodInfo _getPersonality;

        internal static bool Loaded => Chainloader.PluginInfos.ContainsKey(Guid);

        internal static void Enable()
        {
            if (!Loaded)
            {
                return;
            }
            try
            {
                EnablePatch();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("SAIN is loaded but its personality method was not found; restored bots get new personalities. " + e.Message);
            }
        }

        /// <summary>Kept apart so nothing touches SAIN's types unless SAIN is loaded.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void EnablePatch() => new SainPersonalityPatch().Enable();

        /// <summary>SAIN.Interop.SAINExternal.GetPersonality(BotOwner): the personality's name, empty if SAIN has no component on the bot.</summary>
        internal static string ReadPersonality(BotOwner owner)
        {
            if (!_looked)
            {
                _looked = true;
                if (Loaded)
                {
                    _getPersonality = AccessTools.Method("SAIN.Interop.SAINExternal:GetPersonality", new[] { typeof(BotOwner) });
                }
            }
            if (_getPersonality == null)
            {
                return null;
            }
            try
            {
                var name = _getPersonality.Invoke(null, new object[] { owner }) as string;
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    internal sealed class SainPersonalityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(PersonalityDictionary), nameof(PersonalityDictionary.GetPersonality),
                new[] { typeof(SAINBotInfoClass), typeof(PersonalitySettingsClass).MakeByRefType() });

        [PatchPostfix]
        private static void Postfix(PersonalityDictionary __instance, SAINBotInfoClass __0, ref PersonalitySettingsClass __1, ref EPersonality __result)
        {
            if (SainCompat.Pinned.Count == 0)
            {
                return;
            }
            var id = __0?.BotOwner?.ProfileId;
            if (id == null || !SainCompat.Pinned.TryGetValue(id, out var name)
                || !Enum.TryParse<EPersonality>(name, out var personality)
                || !__instance.TryGetValue(personality, out var settings) || settings == null)
            {
                return;
            }
            if (__result != personality && SainCompat.Logged.Add(id))
            {
                Plugin.Log.LogInfo($"SAIN personality kept for {__0.BotOwner.Profile?.Nickname} {id}: {personality} (SAIN had drawn {__result}).");
            }
            else if (SainCompat.Logged.Add(id))
            {
                Plugin.Log.LogInfo($"SAIN personality kept for {__0.BotOwner.Profile?.Nickname} {id}: {personality}.");
            }
            __1 = settings;
            __result = personality;
        }
    }
}
