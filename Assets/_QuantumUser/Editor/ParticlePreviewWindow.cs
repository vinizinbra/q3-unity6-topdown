using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuantumUser.Editor
{
    /// <summary>
    /// Standalone particle previewer - lets an artist play/scrub/orbit any ParticleSystem
    /// prefab in an isolated PreviewRenderUtility scene, with no need to drop it into
    /// QuantumGameScene or enter Play Mode. Drag a prefab into the object field, or Scan
    /// the project for every prefab carrying a ParticleSystem and pick one from the list.
    /// </summary>
    public sealed class ParticlePreviewWindow : EditorWindow
    {
        [MenuItem("Tools/Art/Particle Preview")]
        private static void Open()
        {
            var window = GetWindow<ParticlePreviewWindow>("Particle Preview");
            window.minSize = new Vector2(560, 420);
        }

        // ---- Preview scene ----
        private PreviewRenderUtility _previewUtility;
        private GameObject _previewInstance;
        private GameObject _targetPrefab;
        private readonly List<ParticleSystem> _rootParticleSystems = new();
        private readonly List<ParticleSystem> _allParticleSystems = new();

        // ---- Orbit camera ----
        private Vector2 _orbit = new(35f, -35f); // pitch, yaw
        private float _distance = 5f;
        private Vector3 _pivot = Vector3.zero;

        // ---- Playback ----
        private bool _isPlaying;
        private float _playbackTime;
        private float _playbackSpeed = 1f;
        private double _lastEditorTime;

        // ---- Browser ----
        private string _searchFilter = "";
        private Vector2 _listScroll;
        private readonly List<string> _foundPrefabPaths = new();
        private bool _hasScanned;
        private bool _syncWithSelection = true;

        private static GUIStyle _emptyStateLabel;
        private static GUIStyle EmptyStateLabel => _emptyStateLabel ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = Color.gray }
        };

        private void OnEnable()
        {
            _previewUtility = new PreviewRenderUtility();
            _previewUtility.cameraFieldOfView = 30f;
            _previewUtility.camera.farClipPlane = 100f;
            _previewUtility.camera.nearClipPlane = 0.05f;
            _previewUtility.camera.cullingMask = -1;
            _previewUtility.ambientColor = new Color(0.2f, 0.2f, 0.2f);

            if (_previewUtility.lights.Length > 0)
            {
                _previewUtility.lights[0].intensity = 1.2f;
                _previewUtility.lights[0].transform.rotation = Quaternion.Euler(50f, 50f, 0f);
            }
            if (_previewUtility.lights.Length > 1)
            {
                _previewUtility.lights[1].intensity = 0.6f;
                _previewUtility.lights[1].transform.rotation = Quaternion.Euler(-30f, 220f, 0f);
            }

            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
            _lastEditorTime = EditorApplication.timeSinceStartup;

            // The preview instance is HideAndDontSave and does not survive a domain reload -
            // only the prefab reference does (EditorWindow serializes Object refs). Rebuild it.
            if (_targetPrefab != null)
            {
                var prefab = _targetPrefab;
                _targetPrefab = null;
                SetTarget(prefab);
            }
            else
            {
                OnSelectionChanged();
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Selection.selectionChanged -= OnSelectionChanged;
            DestroyPreviewInstance();
            _previewUtility?.Cleanup();
            _previewUtility = null;
        }

        private void OnSelectionChanged()
        {
            if (!_syncWithSelection) return;

            var go = Selection.activeGameObject;
            if (go == null) return;
            if (!AssetDatabase.Contains(go)) return; // ignore scene objects, only project prefabs
            if (go.GetComponentInChildren<ParticleSystem>(true) == null) return;

            SetTarget(go);
            Repaint();
        }

        private void DestroyPreviewInstance()
        {
            if (_previewInstance != null)
            {
                DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }
            _rootParticleSystems.Clear();
            _allParticleSystems.Clear();
        }

        private void SetTarget(GameObject prefab)
        {
            if (prefab == _targetPrefab) return;

            _targetPrefab = prefab;
            DestroyPreviewInstance();
            _isPlaying = false;
            _playbackTime = 0f;

            if (_targetPrefab == null) return;

            _previewInstance = Instantiate(_targetPrefab);
            _previewUtility.AddSingleGO(_previewInstance);

            _allParticleSystems.AddRange(_previewInstance.GetComponentsInChildren<ParticleSystem>(true));
            foreach (var ps in _allParticleSystems)
            {
                var parent = ps.transform.parent;
                var hasParentPs = parent != null && parent.GetComponentInParent<ParticleSystem>(true) != null;
                if (!hasParentPs) _rootParticleSystems.Add(ps);
            }

            FrameTarget();
            Play();
        }

        private void FrameTarget()
        {
            if (_previewInstance == null) return;

            var renderers = _previewInstance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                _distance = 5f;
                _pivot = Vector3.zero;
                return;
            }

            // Warm the simulation briefly so bounds reflect the particles' actual spread,
            // not just the emitter's origin point.
            SimulateAbsolute(1.5f);

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            _pivot = bounds.center;
            _distance = Mathf.Max(bounds.size.magnitude, 1f) * 1.25f;

            SimulateAbsolute(0f);
        }

        private void SimulateAbsolute(float t)
        {
            foreach (var ps in _rootParticleSystems)
            {
                ps.Simulate(t, true, true, false);
            }
            _playbackTime = t;
        }

        private void Play()
        {
            if (_rootParticleSystems.Count == 0) return;
            SimulateAbsolute(0f);
            _isPlaying = true;
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void PauseToggle()
        {
            _isPlaying = !_isPlaying;
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void Stop()
        {
            _isPlaying = false;
            SimulateAbsolute(0f);
        }

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var delta = (float)(now - _lastEditorTime);
            _lastEditorTime = now;

            if (_isPlaying && _rootParticleSystems.Count > 0)
            {
                var dt = delta * _playbackSpeed;
                _playbackTime += dt;
                foreach (var ps in _rootParticleSystems)
                {
                    ps.Simulate(dt, true, false, false);
                }
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawBrowserPanel();
            DrawPreviewPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBrowserPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            EditorGUILayout.LabelField("Particle Prefabs", EditorStyles.boldLabel);

            _syncWithSelection = EditorGUILayout.ToggleLeft("Sync With Selection", _syncWithSelection);

            var newPrefab = (GameObject)EditorGUILayout.ObjectField(_targetPrefab, typeof(GameObject), false);
            if (newPrefab != _targetPrefab) SetTarget(newPrefab);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("Scan", GUILayout.Width(45))) ScanProject();
            EditorGUILayout.EndHorizontal();

            if (!_hasScanned)
            {
                EditorGUILayout.HelpBox("Click Scan to find every ParticleSystem prefab in the project.", MessageType.Info);
            }

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            foreach (var path in _foundPrefabPaths)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    name.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var isSelected = _targetPrefab != null && AssetDatabase.GetAssetPath(_targetPrefab) == path;
                if (GUILayout.Button(name, isSelected ? EditorStyles.boldLabel : EditorStyles.label))
                {
                    SetTarget(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void ScanProject()
        {
            _foundPrefabPaths.Clear();
            var guids = AssetDatabase.FindAssets("t:GameObject");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponentInChildren<ParticleSystem>(true) != null)
                {
                    _foundPrefabPaths.Add(path);
                }
            }
            _foundPrefabPaths.Sort();
            _hasScanned = true;
        }

        private void DrawPreviewPanel()
        {
            EditorGUILayout.BeginVertical();

            var rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            HandleOrbitInput(rect);
            DrawPreview(rect);

            DrawPlaybackControls();

            EditorGUILayout.EndVertical();
        }

        private void HandleOrbitInput(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                _orbit.y += e.delta.x * 0.5f;
                _orbit.x = Mathf.Clamp(_orbit.x - e.delta.y * 0.5f, -89f, 89f);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.ScrollWheel)
            {
                _distance = Mathf.Max(0.2f, _distance + e.delta.y * 0.2f);
                e.Use();
                Repaint();
            }
        }

        private void DrawPreview(Rect rect)
        {
            if (_targetPrefab == null || _previewInstance == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
                GUI.Label(rect, "Select or drag a Particle System prefab to preview.", EmptyStateLabel);
                return;
            }

            if (Event.current.type != EventType.Repaint) return;

            var rotation = Quaternion.Euler(_orbit.x, _orbit.y, 0f);
            var camPos = _pivot + rotation * new Vector3(0f, 0f, -_distance);

            _previewUtility.BeginPreview(rect, GUIStyle.none);
            _previewUtility.camera.transform.position = camPos;
            _previewUtility.camera.transform.LookAt(_pivot);
            _previewUtility.camera.Render();
            var tex = _previewUtility.EndPreview();
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
        }

        private void DrawPlaybackControls()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(_targetPrefab == null))
            {
                if (GUILayout.Button(_isPlaying ? "Pause" : "Play", EditorStyles.toolbarButton, GUILayout.Width(50)))
                {
                    PauseToggle();
                }
                if (GUILayout.Button("Restart", EditorStyles.toolbarButton, GUILayout.Width(55)))
                {
                    Play();
                }
                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(45)))
                {
                    Stop();
                }
                if (GUILayout.Button("Frame", EditorStyles.toolbarButton, GUILayout.Width(50)))
                {
                    FrameTarget();
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("Speed", GUILayout.Width(40));
            _playbackSpeed = GUILayout.HorizontalSlider(_playbackSpeed, 0.1f, 3f, GUILayout.Width(100));
            GUILayout.Label($"{_playbackSpeed:0.00}x", GUILayout.Width(40));

            GUILayout.FlexibleSpace();

            var particleCount = 0;
            foreach (var ps in _allParticleSystems)
            {
                particleCount += ps.particleCount;
            }
            GUILayout.Label($"t={_playbackTime:0.00}s   particles={particleCount}");

            EditorGUILayout.EndHorizontal();
        }
    }
}
