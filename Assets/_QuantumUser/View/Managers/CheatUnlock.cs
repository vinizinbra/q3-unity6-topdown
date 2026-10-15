#if CHEATS_ENABLED
using UnityEngine;

namespace QuantumUser.View
{
    // Shared "is the cheat UI revealed" state for CheatMenu and OfflineTestPartyPanel. On a player
    // build everything stays hidden until the tap code is entered (screen corners: top-left, top-right,
    // bottom-right, bottom-left, each tap within TapTimeout of the last; a wrong tap restarts), in the
    // menu and in a match alike. CheatMenu's "Hide" button calls Lock() to hide it all again until the
    // code is re-entered. The Editor starts unlocked. Input.mousePosition/GetMouseButtonDown also
    // cover touch (simulateMouseWithTouches).
    public class CheatUnlock : MonoBehaviour
    {
#if UNITY_EDITOR
        public static bool Unlocked { get; private set; } = true;
#else
        public static bool Unlocked { get; private set; }
#endif

        public static event System.Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameObject host = new GameObject("CheatUnlock");
            host.AddComponent<CheatUnlock>();
            DontDestroyOnLoad(host);
        }

        public static void Lock()
        {
            if (Unlocked == false)
                return;

            Unlocked = false;
            Changed?.Invoke();
        }

        private const float CornerSize = 0.15f;
        private const float TapTimeout = 2f;

        private int _step;
        private float _lastTapTime;

        private void Update()
        {
            if (Unlocked || UnityEngine.Input.GetMouseButtonDown(0) == false)
                return;

            if (_step > 0 && Time.unscaledTime - _lastTapTime > TapTimeout)
                _step = 0;

            int corner = CornerAt(UnityEngine.Input.mousePosition);
            _lastTapTime = Time.unscaledTime;

            if (corner == _step)
                _step++;
            else
                _step = corner == 0 ? 1 : 0;

            if (_step < 4)
                return;

            _step = 0;
            Unlocked = true;
            Changed?.Invoke();
        }

        // 0 = top-left, 1 = top-right, 2 = bottom-right, 3 = bottom-left; -1 = no corner.
        private static int CornerAt(Vector2 pos)
        {
            float x = pos.x / Screen.width;
            float y = pos.y / Screen.height;
            bool left = x <= CornerSize;
            bool right = x >= 1f - CornerSize;
            bool bottom = y <= CornerSize;
            bool top = y >= 1f - CornerSize;

            if (top && left) return 0;
            if (top && right) return 1;
            if (bottom && right) return 2;
            if (bottom && left) return 3;
            return -1;
        }
    }
}
#endif
