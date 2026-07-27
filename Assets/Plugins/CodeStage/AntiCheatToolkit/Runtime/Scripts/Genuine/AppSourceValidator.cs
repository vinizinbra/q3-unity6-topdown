#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

using CodeStage.AntiCheat.Genuine.Android;
using CodeStage.AntiCheat.Genuine.Apple;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using CodeStage.AntiCheat.Common;
using CodeStage.AntiCheat.Utils;
#endif

namespace CodeStage.AntiCheat.Genuine
{
	/// <summary>
	/// Cross-platform tool to figure out which store the app was installed from, on Android and iOS.
	/// This is the single entry point for installation source checks; the detailed per-platform sources are
	/// available through <see cref="TryGetAndroidSource"/> and <see cref="TryGetAppleSource"/>.
	/// </summary>
	/// <remarks>
	/// Handy when you ship a single binary and need to branch on the store the app was installed from
	/// (for example, to pick which In-App Purchase SDK to activate). On iOS the resolution runs asynchronously
	/// at launch because it relies on async StoreKit and MarketplaceKit APIs, so a very early call may report
	/// <see cref="AppleAppSource.Unknown"/>; use <see cref="IsResolved"/> to check readiness, or query a bit
	/// later in the app lifecycle.
	/// </remarks>
	public static class AppSourceValidator
	{
		/// <summary>
		/// True when the installation source is ready to read. Always true on platforms other than iOS;
		/// on iOS it reflects the asynchronous launch-time resolution.
		/// </summary>
		public static bool IsResolved
		{
			get
			{
#if UNITY_IOS && !UNITY_EDITOR
				return AppleRoutines.IsResolved();
#else
				return true;
#endif
			}
		}

		/// <summary>
		/// Gets a normalized, cross-platform snapshot of the app installation source.
		/// </summary>
		/// <returns>An <see cref="AppSourceInfo"/> for the current platform; never null.</returns>
		public static AppSourceInfo GetAppSource()
		{
			switch (Application.platform)
			{
				case RuntimePlatform.Android:
				{
					if (TryGetAndroidSource(out var source))
						return new AppSourceInfo(RuntimePlatform.Android, source.PackageName,
							source.DetectedSource == AndroidAppSource.GooglePlayStore, source.DetectedSource, null);
					return new AppSourceInfo(RuntimePlatform.Android, null, false, null, null);
				}
				case RuntimePlatform.IPhonePlayer:
				{
					if (TryGetAppleSource(out var source))
						return new AppSourceInfo(RuntimePlatform.IPhonePlayer, source.MarketplaceBundleId,
							source.DetectedSource == AppleAppSource.AppStore, null, source.DetectedSource);
					return new AppSourceInfo(RuntimePlatform.IPhonePlayer, null, false, null, null);
				}
				default:
					return new AppSourceInfo(Application.platform, null, false, null, null);
			}
		}

		/// <summary>
		/// Checks if app was installed from the platform's first-party store (Google Play / App Store).
		/// </summary>
		/// <returns>True if installed from the official store, false otherwise or if it couldn't be determined.</returns>
		public static bool IsInstalledFromOfficialStore()
		{
			return GetAppSource().IsOfficialStore;
		}

		/// <summary>
		/// Checks if app was installed from the Epic Games Store, on either Android or iOS.
		/// </summary>
		/// <returns>True if app was installed from the Epic Games Store, false otherwise or if it couldn't be determined.</returns>
		public static bool IsInstalledFromEpicGamesStore()
		{
			var info = GetAppSource();
			return info.Android == AndroidAppSource.EpicGamesStore || info.Apple == AppleAppSource.EpicGamesStore;
		}

		/// <summary>
		/// Gets the detailed Android-specific installation source (the installer package name and detected store).
		/// </summary>
		/// <param name="source">The Android installation source when running on an Android device; otherwise null.</param>
		/// <returns>True when running on an Android device and the source was obtained; false otherwise.</returns>
		public static bool TryGetAndroidSource(out AppInstallationSource source)
		{
			source = Application.platform == RuntimePlatform.Android
				? AppInstallationSourceValidator.GetAppInstallationSource()
				: null;
			return source != null;
		}

		/// <summary>
		/// Gets the detailed Apple-specific installation source (iOS / iPadOS), including the signing
		/// <see cref="AppleAppInstallationSource.Environment"/> and the alternative marketplace bundle id.
		/// </summary>
		/// <param name="source">The Apple installation source when running on an iOS device; otherwise null.</param>
		/// <returns>
		/// True when running on an iOS device and the source was obtained; false otherwise. A true result only means
		/// the call ran on iOS, not that the asynchronous resolution finished — check <see cref="IsResolved"/>
		/// (or <see cref="AppleAppSource.Unknown"/>) when you need a fully resolved value.
		/// </returns>
		public static bool TryGetAppleSource(out AppleAppInstallationSource source)
		{
			source = GetAppleSource();
			return source != null;
		}

		private static AppleAppInstallationSource GetAppleSource()
		{
#if UNITY_IOS && !UNITY_EDITOR
			try
			{
				AppleRoutines.Start();
				var distributorKind = AppleRoutines.GetDistributorKind();
				var marketplaceBundleId = AppleRoutines.GetMarketplaceBundleId();
				var environmentKind = AppleRoutines.GetEnvironment();
				var receiptKind = AppleRoutines.GetReceiptKind();
				return new AppleAppInstallationSource(distributorKind, marketplaceBundleId, environmentKind, receiptKind);
			}
			catch (System.Exception e)
			{
				ACTk.PrintExceptionForSupport($"{ACTk.LogPrefix} Error while getting app installation source! See more info below.", e);
			}
#endif
			return null;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void AutoStart()
		{
#if UNITY_IOS && !UNITY_EDITOR
			AppleRoutines.Start();
#endif
		}
	}
}
