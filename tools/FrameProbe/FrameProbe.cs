using System;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Profiling;

namespace SPSave.FrameProbe
{
    /// <summary>
    /// Records frame time, Mono heap size and gen-0 collection count for every raid frame after a warm-up, into arrays
    /// allocated before recording starts, and writes them when the raid ends. Install it for both the SPSave-on and
    /// SPSave-off runs so its own cost is the same on each side; each summary row says whether SPSave was loaded.
    /// </summary>
    [BepInPlugin("com.mybutthasarash.spsave.frameprobe", "SPSave FrameProbe", "0.1.0")]
    public sealed class FrameProbePlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<float> WarmupSeconds;
        internal static ConfigEntry<int> MaxFrames;
        internal static ConfigEntry<string> RunLabel;

        private void Awake()
        {
            Log = Logger;
            WarmupSeconds = Config.Bind("Probe", "Warm-up (seconds)", 30f, "Frames in the first seconds of a raid are not recorded.");
            MaxFrames = Config.Bind("Probe", "Max frames", 600_000, "About an hour at 165 fps. Recording stops when full.");
            RunLabel = Config.Bind("Probe", "Run label", "", "Free text written into each summary row, e.g. the route you played.");
            new Harmony("com.mybutthasarash.spsave.frameprobe").Patch(
                AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted)),
                postfix: new HarmonyMethod(typeof(FrameProbePlugin), nameof(OnGameStartedPostfix)));
        }

        private static void OnGameStartedPostfix(GameWorld __instance)
        {
            if (Singleton<IBotGame>.Instantiated && __instance.GetComponent<FrameRecorder>() == null)
            {
                __instance.gameObject.AddComponent<FrameRecorder>();
            }
        }
    }

    internal sealed class FrameRecorder : MonoBehaviour
    {
        private float[] _frameMs;
        private long[] _monoBytes;
        private int[] _gen0;
        private int _count;
        private float _startAt;
        private string _map;

        private void Awake()
        {
            var capacity = Math.Max(1000, FrameProbePlugin.MaxFrames.Value);
            _frameMs = new float[capacity];
            _monoBytes = new long[capacity];
            _gen0 = new int[capacity];
            _startAt = Time.realtimeSinceStartup + FrameProbePlugin.WarmupSeconds.Value;
            _map = Singleton<GameWorld>.Instance?.LocationId ?? "unknown";
        }

        private void Update()
        {
            if (_count >= _frameMs.Length || Time.realtimeSinceStartup < _startAt)
            {
                return;
            }
            _frameMs[_count] = Time.unscaledDeltaTime * 1000f;
            _monoBytes[_count] = Profiler.GetMonoUsedSizeLong();
            _gen0[_count] = GC.CollectionCount(0);
            _count++;
        }

        private void OnDestroy()
        {
            if (_count < 2)
            {
                return;
            }
            try
            {
                Write();
            }
            catch (Exception e)
            {
                FrameProbePlugin.Log.LogError("FrameProbe could not write its results: " + e);
            }
        }

        private void Write()
        {
            var c = CultureInfo.InvariantCulture;
            var spsave = Chainloader.PluginInfos.ContainsKey("com.mybutthasarash.spsave");
            var root = Path.Combine(BepInEx.Paths.GameRootPath, "SPSave", "frameprobe");
            Directory.CreateDirectory(root);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", c);
            var label = FrameProbePlugin.RunLabel.Value ?? "";

            var raw = new StringBuilder("frame,frame_ms,mono_bytes,gen0\n");
            for (var i = 0; i < _count; i++)
            {
                raw.Append(i.ToString(c)).Append(',').Append(_frameMs[i].ToString("F3", c)).Append(',')
                   .Append(_monoBytes[i].ToString(c)).Append(',').Append(_gen0[i].ToString(c)).Append('\n');
            }
            File.WriteAllText(Path.Combine(root, $"{stamp}-{_map}-{(spsave ? "spsave-on" : "spsave-off")}.csv"), raw.ToString());

            var sorted = new float[_count];
            Array.Copy(_frameMs, sorted, _count);
            Array.Sort(sorted);
            double total = 0;
            int over33 = 0, over50 = 0, over100 = 0;
            for (var i = 0; i < _count; i++)
            {
                total += sorted[i];
                if (sorted[i] > 33.4f) over33++;
                if (sorted[i] > 50f) over50++;
                if (sorted[i] > 100f) over100++;
            }

            // Allocation estimate: heap growth between consecutive frames with no gen-0 collection in between.
            double allocated = 0;
            var counted = 0;
            for (var i = 1; i < _count; i++)
            {
                if (_gen0[i] != _gen0[i - 1])
                {
                    continue;
                }
                var delta = _monoBytes[i] - _monoBytes[i - 1];
                if (delta > 0)
                {
                    allocated += delta;
                }
                counted++;
            }

            var summaryPath = Path.Combine(root, "summary.csv");
            var row = new StringBuilder();
            if (!File.Exists(summaryPath))
            {
                row.Append("utc,map,spsave_loaded,label,frames,seconds,mean_ms,p50_ms,p95_ms,p99_ms,p999_ms,max_ms,over_33ms,over_50ms,over_100ms,gen0_collections,alloc_bytes_per_frame\n");
            }
            row.Append(string.Join(",",
                stamp, _map, spsave ? "1" : "0", "\"" + label.Replace("\"", "\"\"") + "\"",
                _count.ToString(c), (total / 1000d).ToString("F1", c), (total / _count).ToString("F3", c),
                Pct(sorted, 0.50).ToString("F3", c), Pct(sorted, 0.95).ToString("F3", c), Pct(sorted, 0.99).ToString("F3", c),
                Pct(sorted, 0.999).ToString("F3", c), sorted[_count - 1].ToString("F3", c),
                over33.ToString(c), over50.ToString(c), over100.ToString(c),
                (_gen0[_count - 1] - _gen0[0]).ToString(c),
                (counted > 0 ? allocated / counted : 0d).ToString("F0", c))).Append('\n');
            File.AppendAllText(summaryPath, row.ToString());
            FrameProbePlugin.Log.LogInfo($"FrameProbe: {_count} frames on {_map}, SPSave {(spsave ? "loaded" : "not loaded")}, written to {root}");
        }

        private static float Pct(float[] sorted, double p)
        {
            var index = (int)Math.Ceiling(p * sorted.Length) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
        }
    }
}
