#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

namespace CodeStage.AntiCheat.Genuine.Apple
{
	/// <summary>
	/// Signing environment of the app reported by the StoreKit app transaction (iOS 16+).
	/// </summary>
	public enum AppleAppEnvironment
	{
		/// <summary>
		/// Production environment: app transaction signed by the App Store for a real install.
		/// </summary>
		Production = 0,
		/// <summary>
		/// Sandbox environment: TestFlight or a sandbox install.
		/// </summary>
		Sandbox = 1,
		/// <summary>
		/// Xcode environment: app is running from an Xcode build.
		/// </summary>
		Xcode = 2,
		/// <summary>
		/// Environment is unknown: not available (below iOS 16), not resolved yet, or the StoreKit app
		/// transaction failed signature verification (in which case the detected source is reported as
		/// <see cref="AppleAppSource.AccessError"/>).
		/// </summary>
		Unknown = 3
	}
}
