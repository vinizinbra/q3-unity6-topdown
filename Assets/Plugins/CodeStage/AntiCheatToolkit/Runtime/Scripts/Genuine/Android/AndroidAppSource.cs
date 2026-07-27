#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

namespace CodeStage.AntiCheat.Genuine.Android
{
	/// <summary>
	/// Guessed source of the app installation.
	/// </summary>
	public enum AndroidAppSource
	{
		/// <summary>
		/// App was installed on-device from the apk with the package installer.
		/// </summary>
		PackageInstaller = 0,
		GooglePlayStore = 1,
		AmazonAppStore = 2,
		HuaweiAppGallery = 3,
		SamsungGalaxyStore = 4,
		/// <summary>
		/// Source of the app installation is unknown.
		/// </summary>
		Other = 5,
		/// <summary>
		/// There was a problem accessing the app installation source information.
		/// </summary>
		AccessError = 6,
		// New values must be appended with the next free explicit value to keep existing values stable.
		/// <summary>
		/// App was installed from the Epic Games Store.
		/// </summary>
		EpicGamesStore = 7
	}
}