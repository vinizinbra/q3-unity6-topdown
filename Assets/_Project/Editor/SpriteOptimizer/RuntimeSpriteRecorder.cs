using System;
using System.Collections.Generic;
using System.IO;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Project.EditorTools.SpriteOptimizer
{
    [Serializable]
    internal sealed class RuntimeSighting
    {
        public string Guid;
        public long FileId;
        public SpriteContext Context;
        public string Where;
    }

    /// <summary>
    /// Opt-in play-mode sampler: once a second it looks at every live Image / Selectable / SpriteRenderer /
    /// SpriteMask / sprite-sheet ParticleSystem and records which sprite assets were shown in which context.
    /// Catches sprites that code assigns at runtime (upgrade icons, hero portraits, weapon sprites...) which
    /// the static scan can only guess from field rules. Stored in <c>Library/SpriteOptimizer</c> (per machine).
    /// </summary>
    [InitializeOnLoad]
    internal static class RuntimeSpriteRecorder
    {
        private const string PrefKey = "RiftRaiders.SpriteOptimizer.RecordRuntime";
        private const string FilePath = "Library/SpriteOptimizer/RuntimeSightings.json";
        private const double IntervalSeconds = 1.0;

        [Serializable]
        private sealed class Store
        {
            public List<RuntimeSighting> Items = new List<RuntimeSighting>();
        }

        private static Store _store;
        private static HashSet<(string, long, SpriteContext)> _known;
        private static readonly Dictionary<int, SpriteKey?> KeyCache = new Dictionary<int, SpriteKey?>();
        private static double _nextSample;
        private static bool _dirty;

        public static event Action Changed;

        static RuntimeSpriteRecorder()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        public static IReadOnlyList<RuntimeSighting> Sightings
        {
            get
            {
                EnsureLoaded();
                return _store.Items;
            }
        }

        public static void Clear()
        {
            EnsureLoaded();
            _store.Items.Clear();
            _known.Clear();
            Save();
            Changed?.Invoke();
        }

        private static void EnsureLoaded()
        {
            if (_store != null) return;
            _store = new Store();
            try
            {
                if (File.Exists(FilePath))
                    _store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath)) ?? new Store();
            }
            catch (Exception e)
            {
                LogHelper.Warn(SpriteOptimizerUtil.LogTag, $"Could not read runtime sprite recording: {e.Message}");
            }

            _known = new HashSet<(string, long, SpriteContext)>();
            foreach (RuntimeSighting s in _store.Items)
                _known.Add((s.Guid, s.FileId, s.Context));
        }

        private static void Save()
        {
            if (_store == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? "Library");
            File.WriteAllText(FilePath, JsonUtility.ToJson(_store, true));
            _dirty = false;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode && _dirty)
            {
                Save();
                LogHelper.Log(SpriteOptimizerUtil.LogTag, $"Runtime sprite recording saved ({_store.Items.Count} sightings).");
                Changed?.Invoke();
            }
            if (change == PlayModeStateChange.EnteredPlayMode)
                KeyCache.Clear();
        }

        private static void Tick()
        {
            if (!Enabled || !EditorApplication.isPlaying || EditorApplication.isPaused)
                return;
            if (EditorApplication.timeSinceStartup < _nextSample)
                return;
            _nextSample = EditorApplication.timeSinceStartup + IntervalSeconds;

            EnsureLoaded();

            foreach (Image image in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
                Record(image.overrideSprite, SpriteContext.UI, image);

            foreach (Selectable selectable in Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None))
            {
                if (selectable.transition != Selectable.Transition.SpriteSwap) continue;
                SpriteState state = selectable.spriteState;
                Record(state.highlightedSprite, SpriteContext.UI, selectable);
                Record(state.pressedSprite, SpriteContext.UI, selectable);
                Record(state.selectedSprite, SpriteContext.UI, selectable);
                Record(state.disabledSprite, SpriteContext.UI, selectable);
            }

            foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                Record(renderer.sprite, SpriteContext.Gameplay, renderer);

            foreach (SpriteMask mask in Object.FindObjectsByType<SpriteMask>(FindObjectsSortMode.None))
                Record(mask.sprite, SpriteContext.Gameplay, mask);

            foreach (ParticleSystem ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
                if (!sheet.enabled || sheet.mode != ParticleSystemAnimationMode.Sprites) continue;
                for (int i = 0; i < sheet.spriteCount; i++)
                    Record(sheet.GetSprite(i), SpriteContext.Gameplay, ps);
            }
        }

        private static void Record(Sprite sprite, SpriteContext context, Component where)
        {
            if (sprite == null) return;

            int id = sprite.GetInstanceID();
            if (!KeyCache.TryGetValue(id, out SpriteKey? cached))
            {
                cached = SpriteKey.TryGet(sprite, out SpriteKey key) && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(key.Guid))
                    ? key
                    : (SpriteKey?)null;
                KeyCache[id] = cached;
            }
            if (cached == null) return; // Sprite.Create / runtime-generated: nothing to atlas.

            SpriteKey k = cached.Value;
            if (!_known.Add((k.Guid, k.FileId, context))) return;

            Scene scene = where.gameObject.scene;
            _store.Items.Add(new RuntimeSighting
            {
                Guid = k.Guid,
                FileId = k.FileId,
                Context = context,
                Where = $"{scene.name}: {SpriteOptimizerUtil.HierarchyPath(where.transform)} [{where.GetType().Name}]",
            });
            _dirty = true;
        }
    }
}
