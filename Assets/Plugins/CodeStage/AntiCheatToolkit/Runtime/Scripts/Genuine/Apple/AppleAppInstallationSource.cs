#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

namespace CodeStage.AntiCheat.Genuine.Apple
{
	/// <summary>
	/// Holds information about the app installation source on Apple platforms (iOS / iPadOS).
	/// </summary>
	public class AppleAppInstallationSource
	{
		// Raw distributor kinds reported by the native layer (ACTkAppleRoutines.swift). Keep in sync.
		private const int DistributorAppStore = 0;
		private const int DistributorTestFlight = 1;
		private const int DistributorMarketplace = 2;
		private const int DistributorWeb = 3;
		private const int DistributorOther = 4;
		private const int DistributorUnavailable = 5;
		private const int DistributorPending = 6;

		// Raw environment kinds reported by the native layer. Keep in sync.
		private const int EnvironmentProduction = 0;
		private const int EnvironmentSandbox = 1;
		private const int EnvironmentXcode = 2;
		private const int EnvironmentUnavailable = 3;
		private const int EnvironmentPending = 4;
		// Environment 5 = the StoreKit transaction failed verification (.unverified); never fall back to the
		// receipt-name heuristic after a verification failure.
		private const int EnvironmentError = 5;

		// Raw receipt kinds reported by the native layer. Keep in sync.
		private const int ReceiptProduction = 0;
		private const int ReceiptSandbox = 1;
		// Receipt value 2 ("none") has no named constant: it is the implicit default that yields AccessError.

		// Known alternative marketplace bundle ids (returned by AppDistributor.current as .marketplace(bundleId)).
		private const string EpicGamesStoreBundleId = "com.epicgames.store";

		/// <summary>
		/// Bundle id of the alternative marketplace the app was installed from, for example the Epic Games Store.
		/// Empty for non-marketplace installs (App Store, TestFlight, etc.).
		/// </summary>
		public string MarketplaceBundleId { get; }

		/// <summary>
		/// Detected source of the app installation to simplify further processing.
		/// </summary>
		public AppleAppSource DetectedSource { get; }

		/// <summary>
		/// Signing environment reported by the StoreKit app transaction (iOS 16+), when available.
		/// </summary>
		public AppleAppEnvironment Environment { get; }

		internal AppleAppInstallationSource(int distributorKind, string marketplaceBundleId, int environmentKind, int receiptKind)
		{
			MarketplaceBundleId = marketplaceBundleId ?? string.Empty;
			Environment = GetEnvironment(environmentKind);
			DetectedSource = GetSource(distributorKind, MarketplaceBundleId, environmentKind, receiptKind);
		}

		private static AppleAppEnvironment GetEnvironment(int environmentKind)
		{
			switch (environmentKind)
			{
				case EnvironmentProduction:
					return AppleAppEnvironment.Production;
				case EnvironmentSandbox:
					return AppleAppEnvironment.Sandbox;
				case EnvironmentXcode:
					return AppleAppEnvironment.Xcode;
				default:
					return AppleAppEnvironment.Unknown;
			}
		}

		private static AppleAppSource GetSource(int distributorKind, string marketplaceBundleId, int environmentKind, int receiptKind)
		{
			// Resolution still in progress (async at launch); the caller may retry later.
			if (distributorKind == DistributorPending)
				return AppleAppSource.Unknown;

			// Rung 1: MarketplaceKit AppDistributor (authoritative, iOS 17.4+). The only signal that names a marketplace.
			switch (distributorKind)
			{
				case DistributorAppStore:
					return AppleAppSource.AppStore;
				case DistributorTestFlight:
					return AppleAppSource.TestFlight;
				case DistributorMarketplace:
					return GetMarketplaceSource(marketplaceBundleId);
				case DistributorWeb:
					return AppleAppSource.WebDistribution;
				case DistributorOther:
					// .other is Apple's enterprise / education distribution bucket. A genuine enterprise install
					// reports a Production or Sandbox signed environment and stays Other; only refine to Xcode when
					// the signed StoreKit environment actually reports .xcode, and wait while it is still pending.
					if (environmentKind == EnvironmentXcode)
						return AppleAppSource.Xcode;
					if (environmentKind == EnvironmentPending)
						return AppleAppSource.Unknown;
					return AppleAppSource.Other;
			}

			// distributorKind == DistributorUnavailable (below iOS 17.4): fall back to the signed environment signal.
			if (environmentKind == EnvironmentPending)
				return AppleAppSource.Unknown;

			// A failed StoreKit verification (.unverified) must not silently downgrade to the spoofable receipt name.
			if (environmentKind == EnvironmentError)
				return AppleAppSource.AccessError;

			// Rung 2: StoreKit app transaction environment (iOS 16+, cryptographically signed). Carries no marketplace info.
			switch (environmentKind)
			{
				case EnvironmentXcode:
					return AppleAppSource.Xcode;
				case EnvironmentSandbox:
					return AppleAppSource.TestFlight;
				case EnvironmentProduction:
					return AppleAppSource.AppStore;
			}

			// environmentKind == EnvironmentUnavailable (below iOS 16): last resort, the legacy receipt name heuristic.
			switch (receiptKind)
			{
				case ReceiptProduction:
					return AppleAppSource.AppStore;
				case ReceiptSandbox:
					return AppleAppSource.TestFlight;
			}

			// Receipt 'none' (value 2) or anything unexpected: nothing could classify the install.
			return AppleAppSource.AccessError;
		}

		private static AppleAppSource GetMarketplaceSource(string marketplaceBundleId)
		{
			switch (marketplaceBundleId)
			{
				case EpicGamesStoreBundleId:
					return AppleAppSource.EpicGamesStore;
				default:
					return AppleAppSource.AlternativeMarketplace;
			}
		}
	}
}
