using System;
using UnityEngine;
using CodeStage.AntiCheat.ObscuredTypes;
using CodeStage.AntiCheat.Storage;
using QuantumUser.View.Util;

namespace Playtime.Core
{
    [Serializable]
    public abstract class PlayerPrefProperty<T>
    {
        #region --- Inspector ---

        [SerializeField] protected string key;

        #endregion

        #region --- Members ---

        private bool initialized;

        private T value;

        #endregion

        #region --- Properties ---

        /// <summary>
        ///     Use this property to get/set this Player pref.
        /// </summary>
        public T Value
        {
            get
            {
                RetrieveValue();
                return value;
            }
            set => SaveValue(value);
        }

        #endregion

        #region --- Construction ---

        protected PlayerPrefProperty(string prefKey)
        {
            key = prefKey;
            value = default;
            initialized = false;
        }

        #endregion

        #region --- Private Methods ---

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
            LogHelper.Log("PlayerPref", "saving value");
            value = newValue;
            OnSaveValue(value);
        }

        protected abstract T OnRetrieveValue();

        protected abstract void OnSaveValue(T value);

        #endregion
    }

    [Serializable]
    public class PlayerPrefString : PlayerPrefProperty<string>
    {
        #region --- Members ---

        private string defaultValue = "";

        #endregion

        #region --- Construction ---

        public PlayerPrefString(string key) : base(key)
        {
        }

        public PlayerPrefString(string key, string defaultValue) : base(key)
        {
            this.defaultValue = defaultValue;
        }

        #endregion

        #region --- Private Methods ---

        protected override string OnRetrieveValue()
        {
            return ObscuredPrefs.Get(key, defaultValue);
        }

        protected override void OnSaveValue(string value)
        {
            ObscuredPrefs.Set(key, value);
        }

        #endregion
    }

    [Serializable]
    public class PlayerPrefInt : PlayerPrefProperty<int>
    {
        #region --- Members ---

        private int defaultValue;

        #endregion

        #region --- Construction ---

        public PlayerPrefInt(string key) : base(key)
        {
        }

        public PlayerPrefInt(string key, int defaultValue) : base(key)
        {
            this.defaultValue = defaultValue;
        }

        #endregion

        #region --- Private Methods ---

        protected override int OnRetrieveValue()
        {
            return ObscuredPrefs.Get(key, defaultValue);
        }

        protected override void OnSaveValue(int value)
        {
            ObscuredPrefs.Set(key, value);
        }

        #endregion
    }

    [Serializable]
    public class PlayerPrefFloat : PlayerPrefProperty<float>
    {
        #region --- Members ---

        private float defaultValue;

        #endregion

        #region --- Construction ---

        public PlayerPrefFloat(string key) : base(key)
        {
        }

        public PlayerPrefFloat(string key, float defaultValue) : base(key)
        {
            this.defaultValue = defaultValue;
        }

        #endregion

        #region --- Private Methods ---

        protected override float OnRetrieveValue()
        {
            return ObscuredPrefs.Get(key, defaultValue);
        }

        protected override void OnSaveValue(float value)
        {
            ObscuredPrefs.Set(key, value);
        }

        #endregion
    }

    [Serializable]
    public class PlayerPrefBool : PlayerPrefProperty<bool>
    {
        #region --- Members ---

        private bool defaultValue;

        #endregion

        #region --- Construction ---

        public PlayerPrefBool(string key) : base(key)
        {
        }

        public PlayerPrefBool(string key, bool defaultValue) : base(key)
        {
            this.defaultValue = defaultValue;
        }

        #endregion

        #region --- Private Methods ---

        protected override bool OnRetrieveValue()
        {
            return ObscuredPrefs.Get(key, defaultValue ? 1 : 0) != 0;
        }

        protected override void OnSaveValue(bool value)
        {
            ObscuredPrefs.Set(key, value ? 1 : 0);
        }

        #endregion
    }

    [Serializable]
    public class PlayerPrefObject<T> : PlayerPrefProperty<T> where T : class
    {
        #region --- Members ---

        private T defaultValue;

        #endregion

        #region --- Construction ---

        public PlayerPrefObject(string key) : base(key)
        {
        }

        public PlayerPrefObject(string key, T defaultValue) : base(key)
        {
            this.defaultValue = defaultValue;
        }

        #endregion

        #region --- Private Methods ---

        protected override T OnRetrieveValue()
        {
            var json = ObscuredPrefs.Get(key, null);
            if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<T>(json);

            return defaultValue;
        }

        protected override void OnSaveValue(T value)
        {
            var json = JsonUtility.ToJson(value);
            ObscuredPrefs.Set(key, json);
        }

        #endregion

        public void Save()
        {
            SaveValue(this.Value);
        }
    }
}