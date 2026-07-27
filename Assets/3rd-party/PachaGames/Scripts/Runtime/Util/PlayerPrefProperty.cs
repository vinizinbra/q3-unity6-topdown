using System;
using UnityEngine;
using CodeStage.AntiCheat.ObscuredTypes;
using CodeStage.AntiCheat.Storage;


namespace PG
{
    public static class PgPlayerPref
    {
        public static bool HasKey(string hasKey)
        {
            return ObscuredPrefs.HasKey(hasKey);
        }
    }
    [Serializable]
    public abstract class PlayerPrefProperty<T>
    {
        [SerializeField] protected string key;
        private bool initialized;
        private T value;


        public string Key => key;
        public T Value
        {
            get
            {
                RetrieveValue();
                return value;
            }
            set => SaveValue(value);
        }

        protected PlayerPrefProperty(string prefKey)
        {
            key = prefKey;
            value = default;
            initialized = false;
        }

        protected void RetrieveValue()
        {
            if (!initialized)
            {
                value = OnRetrieveValue();
                initialized = true;
            }
        }

        public void SaveValue(T newValue)
        {
            value = newValue;
            OnSaveValue(value);
        }

        protected abstract T OnRetrieveValue();
        protected abstract void OnSaveValue(T value);
    }

    [Serializable]
    public class PlayerPrefString : PlayerPrefProperty<string>
    {
        private string defaultValue = "";

        public PlayerPrefString(string key) : base(key) { }
        public PlayerPrefString(string key, string defaultValue) : base(key) => this.defaultValue = defaultValue;

        protected override string OnRetrieveValue()
        {
            return ObscuredPrefs.Get(key, defaultValue);
        }

        protected override void OnSaveValue(string value)
        {
            ObscuredPrefs.Set(key, value);
        }
    }

    [Serializable]
    public class PlayerPrefInt : PlayerPrefProperty<int>
    {
        private int defaultValue;

        public PlayerPrefInt(string key) : base(key) { }
        public PlayerPrefInt(string key, int defaultValue) : base(key) => this.defaultValue = defaultValue;

        protected override int OnRetrieveValue()
        {
            return ObscuredPrefs.Get(key, defaultValue);
        }

        protected override void OnSaveValue(int value)
        {
            ObscuredPrefs.Set(key, value);
        }
    }

    [Serializable]
    public class PlayerPrefDate : PlayerPrefProperty<DateTime>
    {
        private DateTime defaultValue;
        public new DateTime Value
        {
            get => base.Value;
            set => base.Value = value;
        }
        public PlayerPrefDate(string key) : base(key)
        {
            defaultValue = DateTime.MinValue;
        }

        public PlayerPrefDate(string key, DateTime defaultValue) : base(key)
        {
            this.defaultValue = defaultValue;
        }

        protected override DateTime OnRetrieveValue()
        {
            string stored = null;

            try
            {
                stored = ObscuredPrefs.Get(key, null);

                if (!string.IsNullOrEmpty(stored))
                {
                    if (DateTime.TryParse(stored, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                        return parsed;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayerPrefDate] Failed to parse key '{key}' with value '{stored}': {e.Message}");
            }

            // fallback
            return defaultValue;
        }

        protected override void OnSaveValue(DateTime value)
        {
            string iso = value.ToString("o"); // ISO 8601

            try
            {
                ObscuredPrefs.Set(key, iso);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerPrefDate] Failed to save key '{key}' with value '{iso}': {e.Message}");
            }
        }

        /// <summary>
        /// Resets the value to default and saves.
        /// </summary>
        public void ResetToDefault()
        {
            SaveValue(defaultValue);
        }
    }

    [Serializable]
    public class PlayerPrefFloat : PlayerPrefProperty<float>
    {
        private float defaultValue;

        public PlayerPrefFloat(string key) : base(key) { }
        public PlayerPrefFloat(string key, float defaultValue) : base(key) => this.defaultValue = defaultValue;

        protected override float OnRetrieveValue()
        {
            return ObscuredPrefs.Get(key, defaultValue);
        }

        protected override void OnSaveValue(float value)
        {
            ObscuredPrefs.Set(key, value);
        }
    }

    [Serializable]
    public class PlayerPrefBool : PlayerPrefProperty<bool>
    {
        private bool defaultValue;

        public PlayerPrefBool(string key) : base(key) { }
        public PlayerPrefBool(string key, bool defaultValue) : base(key) => this.defaultValue = defaultValue;

        protected override bool OnRetrieveValue()
        {
            return ObscuredPrefs.Get(key, defaultValue ? 1 : 0) != 0;
        }

        protected override void OnSaveValue(bool value)
        {
            ObscuredPrefs.Set(key, value ? 1 : 0);
        }
    }

    [Serializable]
    public class PlayerPrefObject<T> : PlayerPrefProperty<T> where T : class
    {
        private T defaultValue;

        public PlayerPrefObject(string key) : base(key) { }
        public PlayerPrefObject(string key, T defaultValue) : base(key) => this.defaultValue = defaultValue;

        protected override T OnRetrieveValue()
        {
            string json = ObscuredPrefs.Get(key, null);
            if (!string.IsNullOrEmpty(json))
            {
                return JsonUtility.FromJson<T>(json);
            }

            return defaultValue;
        }

        protected override void OnSaveValue(T value)
        {
            string json = JsonUtility.ToJson(value);
            ObscuredPrefs.Set(key, json);
        }

        public void Save()
        {
            SaveValue(this.Value);
        }

    }
}
