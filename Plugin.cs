using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;

namespace BlockSoundReplacer
{
    [BepInPlugin(GUID, "Block Sound Replacer", "0.5.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "local.blocksoundreplacer";

        internal static Plugin Instance;
        internal static AudioClip Clip;
        internal static ConfigEntry<string> FileName;
        internal static ConfigEntry<bool> ReplaceBlock;
        internal static ConfigEntry<bool> ReplaceParry;
        internal static ConfigEntry<bool> ReplaceBarrierHit;
        internal static ConfigEntry<bool> DirectPlayback;
        internal static ConfigEntry<float> Volume;

        private readonly HashSet<GameObject> _done = new HashSet<GameObject>();
        private readonly HashSet<string> _seen = new HashSet<string>();

        // Prefabs replaced through the game's own sound component (barrier): volume is scaled at prefab level.
        private readonly List<GameObject> _zsfxPrefabs = new List<GameObject>();
        private readonly Dictionary<ZSFX, float[]> _zsfxBase = new Dictionary<ZSFX, float[]>();
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo MinVol = typeof(ZSFX).GetField("m_minVol", AnyInstance);
        private static readonly FieldInfo MaxVol = typeof(ZSFX).GetField("m_maxVol", AnyInstance);
        private float _lastVolume = -1f;

        internal static void Log(string msg) { Instance.Logger.LogInfo(msg); }

        private void Awake()
        {
            Instance = this;
            FileName = Config.Bind("General", "FileName", "hitsound.wav", "Audio file next to the DLL. WAV or OGG.");
            ReplaceBlock = Config.Bind("General", "ReplaceBlock", false, "Replace the normal block sound (shields and weapons).");
            ReplaceParry = Config.Bind("General", "ReplaceParry", true, "Replace the parry / perfect-block sound.");
            ReplaceBarrierHit = Config.Bind("General", "ReplaceBarrierHit", true, "Replace the Staff of Protection barrier hit sound.");

            Volume = Config.Bind("General", "Volume", 1f,
                new ConfigDescription("Volume of the replaced sounds. 0 = silent, 1 = full.", new AcceptableValueRange<float>(0f, 1f)));
            DirectPlayback = Config.Bind("General", "DirectPlayback", true, "Play the clip through our own AudioSource when a replaced effect spawns, and mute the original sound.");

            if (MinVol == null || MaxVol == null)
                Logger.LogWarning("ZSFX volume fields (m_minVol/m_maxVol) not found; barrier volume relies on AudioSource volume only.");

            StartCoroutine(LoadClip());
            StartCoroutine(Watch());
        }

        // Apply slider changes immediately, including while the game is running.
        private void Update()
        {
            if (Volume.Value != _lastVolume) { _lastVolume = Volume.Value; ApplyVolume(); }
        }

        private void ApplyVolume()
        {
            float v = Mathf.Clamp01(Volume.Value);
            foreach (var prefab in _zsfxPrefabs)
            {
                if (prefab == null) continue;
                foreach (var src in prefab.GetComponentsInChildren<AudioSource>(true)) src.volume = v;
                foreach (var zs in prefab.GetComponentsInChildren<ZSFX>(true))
                {
                    float[] b;
                    if (!_zsfxBase.TryGetValue(zs, out b)) continue;
                    if (MinVol != null) MinVol.SetValue(zs, b[0] * v);
                    if (MaxVol != null) MaxVol.SetValue(zs, b[1] * v);
                }
            }
        }


        private IEnumerator LoadClip()
        {
            string dir = Path.GetDirectoryName(Info.Location);
            string path = Path.Combine(dir, FileName.Value);
            if (!File.Exists(path))
            {
                Logger.LogError("Audio file not found: " + path);
                yield break;
            }

            var type = path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.WAV;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
            {
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError)
                {
                    Logger.LogError("Failed to load audio: " + req.error);
                    yield break;
                }
                Clip = DownloadHandlerAudioClip.GetContent(req);
                Clip.name = "BlockSoundReplacer_clip";
                Logger.LogInfo("Loaded clip: length " + Clip.length + "s, samples " + Clip.samples + ", channels " + Clip.channels + ", freq " + Clip.frequency + ", loadState " + Clip.loadState);
            }
        }

        // ObjectDB / ZNetScene are recreated on scene changes, so re-apply whenever the instance changes.
        private IEnumerator Watch()
        {
            ObjectDB lastDb = null;
            ZNetScene lastScene = null;
            while (true)
            {
                if (Clip != null)
                {
                    var db = ObjectDB.instance;
                    if (db != null && db != lastDb && db.m_items != null && db.m_items.Count > 0)
                    {
                        lastDb = db;
                        _done.Clear();
                        ApplyItems(db);
                        if (ReplaceBarrierHit.Value) ApplyBarrier(db);
                    }
                    var zs = ZNetScene.instance;
                    if (zs != null && zs != lastScene)
                    {
                        lastScene = zs;
                        ApplyPlayer(zs);
                    }
                }
                yield return new WaitForSeconds(2f);
            }
        }

        // Every item's SharedData: EffectList fields whose name contains "block" (shield/weapon block effects).
        private void ApplyItems(ObjectDB db)
        {
            int items = 0;
            foreach (var go in db.m_items)
            {
                if (go == null) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                if (ScanEffectLists(drop.m_itemData.m_shared, go.name)) items++;
            }
            Logger.LogInfo("Block effects patched on " + items + " item(s).");
        }

        // The player prefab (Humanoid/Character) may hold its own block/parry effect lists.
        private void ApplyPlayer(ZNetScene zs)
        {
            var prefab = zs.GetPrefab("Player");
            if (prefab == null) { Logger.LogWarning("Player prefab not found."); return; }
            foreach (var comp in prefab.GetComponents<MonoBehaviour>())
                if (comp != null) ScanEffectLists(comp, "Player/" + comp.GetType().Name);
        }

        private void ApplyBarrier(ObjectDB db)
        {
            foreach (var se in db.m_StatusEffects)
            {
                if (se == null || se.GetType().Name != "SE_Shield") continue;
                foreach (var f in AllFields(se.GetType()))
                {
                    if (f.FieldType != typeof(EffectList) || f.Name != "m_hitEffects") continue;
                    var list = (EffectList)f.GetValue(se);
                    if (list?.m_effectPrefabs == null) continue;
                    foreach (var ed in list.m_effectPrefabs)
                        if (ed != null && ed.m_prefab != null) Replace(ed.m_prefab, se.name + "." + f.Name, false);
                }
            }
        }

        // Returns true if any effect prefab was modified.
        private bool ScanEffectLists(object owner, string label)
        {
            bool any = false;
            foreach (var f in AllFields(owner.GetType()))
            {
                if (f.FieldType != typeof(EffectList)) continue;
                string n = f.Name.ToLowerInvariant();
                if (n.Contains("dodge")) continue;
                bool isParry = n.Contains("parry") || n.Contains("perfect");
                bool isBlock = n.Contains("block");
                if (!isParry && !isBlock) continue;
                if (_seen.Add(owner.GetType().Name + "." + f.Name))
                    Logger.LogInfo("  found effect list " + owner.GetType().Name + "." + f.Name + " (" + (isParry ? "parry" : "block") + ")");
                if (isParry && !ReplaceParry.Value) continue;
                if (!isParry && !ReplaceBlock.Value) continue;

                var list = f.GetValue(owner) as EffectList;
                if (list?.m_effectPrefabs == null) continue;
                foreach (var ed in list.m_effectPrefabs)
                    if (ed != null && ed.m_prefab != null && Replace(ed.m_prefab, label + "." + f.Name, DirectPlayback.Value)) any = true;
            }
            return any;
        }

        private static IEnumerable<FieldInfo> AllFields(Type t)
        {
            const BindingFlags fl = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            for (; t != null && t != typeof(object) && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (var f in t.GetFields(fl)) yield return f;
        }

        private bool Replace(GameObject prefab, string where, bool direct)
        {
            if (!_done.Add(prefab)) return true;
            int sfx = 0, sources = 0;
            foreach (var zs in prefab.GetComponentsInChildren<ZSFX>(true))
            {
                zs.m_audioClips = new[] { Clip };
                if (!direct && !_zsfxBase.ContainsKey(zs))
                    _zsfxBase[zs] = new[] { MinVol != null ? (float)MinVol.GetValue(zs) : 1f, MaxVol != null ? (float)MaxVol.GetValue(zs) : 1f };
                sfx++;
            }
            foreach (var src in prefab.GetComponentsInChildren<AudioSource>(true)) { src.clip = Clip; sources++; }
            if (!direct)
            {
                if (!_zsfxPrefabs.Contains(prefab)) _zsfxPrefabs.Add(prefab);
                ApplyVolume();
            }
            Logger.LogInfo("  " + where + " -> prefab '" + prefab.name + "': " + sfx + " ZSFX, " + sources + " AudioSource");
            if (direct && sfx + sources > 0 && prefab.GetComponent<EffectProbe>() == null)
            {
                try { prefab.AddComponent<EffectProbe>().Tag = where + "/" + prefab.name; }
                catch (Exception e) { Logger.LogWarning("Could not add probe to " + prefab.name + ": " + e.Message); }
            }
            return sfx + sources > 0;
        }
    }

    public class EffectProbe : MonoBehaviour
    {
        public string Tag;

        private void Start()
        {
            var sources = GetComponentsInChildren<AudioSource>(true);
            foreach (var a in sources)
                Plugin.Log("[probe] spawned " + Tag + ": clip=" + (a.clip != null ? a.clip.name : "null") +
                    " playing=" + a.isPlaying + " mute=" + a.mute + " vol=" + a.volume + " enabled=" + a.enabled +
                    " mixer=" + (a.outputAudioMixerGroup != null ? a.outputAudioMixerGroup.name : "none") + " spatial=" + a.spatialBlend);

            if (!Plugin.DirectPlayback.Value || Plugin.Clip == null) return;

            AudioSource template = sources.Length > 0 ? sources[0] : null;
            foreach (var a in sources) { a.Stop(); a.mute = true; }

            var go = new GameObject("BlockSoundReplacer_fx");
            go.transform.position = transform.position;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.clip = Plugin.Clip;
            src.volume = Mathf.Clamp01(Plugin.Volume.Value);
            if (template != null)
            {
                src.outputAudioMixerGroup = template.outputAudioMixerGroup;
                src.spatialBlend = template.spatialBlend;
                src.minDistance = template.minDistance;
                src.maxDistance = template.maxDistance;
                src.rolloffMode = template.rolloffMode;
            }
            else src.spatialBlend = 0f;
            src.Play();
            Destroy(go, Plugin.Clip.length + 0.5f);
        }
    }
}
