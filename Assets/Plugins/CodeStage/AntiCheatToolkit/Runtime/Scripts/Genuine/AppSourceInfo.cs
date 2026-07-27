#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

using CodeStage.AntiCheat.Genuine.Android;
using CodeStage.AntiCheat.Genuine.Apple;
using UnityEngine;

namespace CodeStage.AntiCheat.Genuine
{
	/// <summary>
	/// Cross-platform snapshot of the app installation source. See <see cref="AppSourceValidator"/>.
	/// </summary>
	public class AppSourceInfo
	{
		/// <summary>
		/// Platform this info was resolved on.
		/// </summary>
		public RuntimePlatform Platform { get; }

		/// <summary>
		/// Raw installation source identifier: the installer package name on Android (e.g. "com.android.vending")
		/// or the alternative marketplace bundle id on iOS. Empty when not applicable (e.g. an App Store install).
		/// </summary>
		public string RawSourceId { get; }

		/// <summary>
		/// True when the app was installed from the platform's first-party store
		/// (Google Play on Android, Apple App Store on iOS).
		/// </summary>
		public bool IsOfficialStore { get; }

		/// <summary>
		/// Android-specific detected source, or null when not running on Android.
		/// </summary>
		public AndroidAppSource? Android { get; }

		/// <summary>
		/// Apple-specific detected source, or null when not running on an Apple platform.
		/// </summary>
		public AppleAppSource? Apple { get; }

		internal AppSourceInfo(RuntimePlatform platform, string rawSourceId, bool isOfficialStore, AndroidAppSource? android, AppleAppSource? apple)
		{
			Platform = platform;
			RawSourceId = rawSourceId ?? string.Empty;
			IsOfficialStore = isOfficialStore;
			Android = android;
			Apple = apple;
		}
	}
}
