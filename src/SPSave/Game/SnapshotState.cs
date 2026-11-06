using System.Collections.Generic;

namespace SPSave.Game
{
    // The mod's own part of a save, written as state.json. Engine-owned data (profiles, world loot) is stored in the
    // game's binary formats in separate sections; see RaidCapture. Plain arrays instead of Unity vectors keep this
    // serializable with stock Newtonsoft and readable in a text editor.

    public sealed class Manifest
    {
        public int Format = 1;
        public string ModVersion;
        public string CreatedUtc;
        public string GameVersion;
        public string SptVersion;
        public string ProfileId;
        public string Side;
        public string Map;
        public string ServerId;
        public string TimeVariant;
        public List<string> Plugins = new List<string>();
    }

    public sealed class RaidState
    {
        public PlayerState Player;
        public List<BotState> Bots = new List<BotState>();
        public List<DoorState> Doors = new List<DoorState>();
        public ClockState Clock;
        public CombatState Combat;
    }

    public sealed class PlayerState
    {
        public string ProfileId;
        public float[] Position;
        /// <summary>Player.Rotation: x yaw, y pitch.</summary>
        public float[] Rotation;
        public float PoseLevel;
        public bool Prone;
        public float Stamina;
        public float HandsStamina;
        public float Oxygen;
        public string HandsItemId;
        public string HandsItemTemplate;
        /// <summary>Items on the player marked found in raid; the resume compares the restored profile with this.</summary>
        public int FoundInRaidItems;
        /// <summary>Every item on the player; the resume checks the rebuilt profile against it.</summary>
        public List<SPSave.Core.ItemRecord> Items = new List<SPSave.Core.ItemRecord>();
        /// <summary>Containers the player had searched (PlayerSearchController._searchedItems), by item id.</summary>
        public List<string> Searched = new List<string>();
        /// <summary>Items the player had seen inside searchable containers (ActiveSearchController._knownItems), by id.</summary>
        public List<string> Known = new List<string>();
        public List<EffectState> Effects = new List<EffectState>();
    }

    public sealed class BotState
    {
        public string ProfileId;
        public string Role;
        public string Side;
        public float[] Position;
        public float[] Rotation;
        public float PoseLevel;
        public bool Prone;
        public string HandsItemId;
        /// <summary>Bots with the same number shared a BotsGroup (numbered per save, from 1; 0 = none).</summary>
        public int Group;
        /// <summary>The group's BotsGroup.TargetMembersCount (ORBIT sizes its squads from it).</summary>
        public int GroupTarget;
        /// <summary>BotBoss.IamBoss, and how many followers it wanted (BotBoss.TargetFollowersCount).</summary>
        public bool Boss;
        public int FollowersTarget;
        /// <summary>BotOwner.SpawnBotZone.NameZone.</summary>
        public string Zone;
        /// <summary>SAIN's personality name (SAINExternal.GetPersonality); null without SAIN.</summary>
        public string Personality;
        public bool KnowsPlayer;
        public bool SeesPlayer;
        public float? SecondsSinceSeenPlayer;
        public float? SecondsSinceShotAtPlayer;
        public List<EffectState> Effects = new List<EffectState>();
    }

    /// <summary>
    /// One active health effect with its timings. Profile.HealthInfo (Store) keeps only IRestorable effects and
    /// drops most timers, so these are what a restore will feed to ActiveHealthController.AddEffect.
    /// </summary>
    public sealed class EffectState
    {
        public string Type;
        public string BodyPart;
        public string State;
        public float StateCurrentTime;
        public float TimeLeft;
        public float DelayTime;
        public float BuildUpTime;
        public float WorkStateTime;
        public float ResidueStateTime;
        public float Strength;
        /// <summary>PainKiller only: the medicine that gave it and its full duration (both runtime-only on the effect).</summary>
        public string ItemTemplateId;
        public float MaxDuration;
    }

    public sealed class DoorState
    {
        public string Id;
        public string Kind;
        public int State;
        /// <summary>Door.IsBroken: breached. Restored through Door.SetInitialSyncState.</summary>
        public bool Broken;
    }

    public sealed class ClockState
    {
        public double PastSeconds;
        public double SessionSeconds;
        public double RemainingSeconds;
        /// <summary>In-game date and time (GameDateTime.Calculate) as ticks, and the factor it runs at.</summary>
        public long GameTimeTicks;
        public string GameTime;
        public float TimeFactor;
        public float TimeFactorMod;
        /// <summary>False on maps without a WeatherController (Factory, for one); then there are no nodes to save.</summary>
        public bool HasWeather;
        public int WeatherNodes;
        public bool WeatherMatchesCurve;
    }

    public sealed class CombatState
    {
        public float WindowSeconds;
        public bool InCombat;
        public string Detail;
    }
}
