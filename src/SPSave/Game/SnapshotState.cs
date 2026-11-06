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
    }

    public sealed class DoorState
    {
        public string Id;
        public string Kind;
        public int State;
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
