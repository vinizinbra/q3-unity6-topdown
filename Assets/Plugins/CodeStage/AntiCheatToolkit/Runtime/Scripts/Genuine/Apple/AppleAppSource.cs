#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

namespace CodeStage.AntiCheat.Genuine.Apple
{
	/// <summary>
	/// Detected source of the app installation on Apple platforms (iOS / iPadOS).
	/// </summary>
	public enum AppleAppSource
	{
		/// <summary>
		/// App was installed from the Apple App Store.
		/// </summary>
		AppStore = 0,
		/// <summary>
		/// App was installed via TestFlight.
		/// </summary>
		/// <remarks>
		/// Below iOS 17.4, detection falls back first to the StoreKit app transaction environment (iOS 16–17.3)
		/// and then to the legacy receipt name (below iOS 16); in both fallbacks any sandbox install is reported
		/// as TestFlight and can't be told apart from other sandbox installs.
		/// </remarks>
		TestFlight = 1,
		/// <summary>
		/// App is running from an Xcode / development build.
		/// </summary>
		Xcode = 2,
		/// <summary>
		/// App was installed from the Epic Games Store (marketplace bundle id "com.epicgames.store").
		/// Requires iOS 17.4+ and is only available in regions where Apple permits alternative distribution.
		/// </summary>
		EpicGamesStore = 3,
		/// <summary>
		/// App was installed from an alternative app marketplace other than the known ones listed above.
		/// Check <see cref="AppleAppInstallationSource.MarketplaceBundleId"/> for the marketplace bundle id.
		/// Requires iOS 17.4+ and is only available in regions where Apple permits alternative distribution.
		/// </summary>
		AlternativeMarketplace = 4,
		/// <summary>
		/// App was installed from the developer's own website (Web Distribution).
		/// </summary>
		WebDistribution = 5,
		/// <summary>
		/// App was installed via an enterprise or education developer program.
		/// </summary>
		Other = 6,
		/// <summary>
		/// Source of the app installation is not resolved yet. Resolution runs asynchronously at launch, so
		/// this value may appear when queried very early; check <see cref="AppSourceValidator.IsResolved"/>
		/// or query again a bit later.
		/// </summary>
		Unknown = 7,
		/// <summary>
		/// There was a problem accessing the app installation source information.
		/// </summary>
		AccessError = 8
		// New values must be appended with the next free explicit value to keep existing values stable.
	}
}
