#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

using CodeStage.AntiCheat.Genuine;
using UnityEngine;

namespace CodeStage.AntiCheat.Examples
{
	// Cross-platform app installation source detection: the same AppSourceValidator API works on Android and iOS.
	internal partial class AppSourceExamples : MonoBehaviour
	{
		private AppSourceInfo AppSourceResult { get; set; }

		// Branch on the store the app came from, for example to pick which In-App Purchase SDK to activate.
		private AppSourceInfo GetInstallationSource()
		{
			if (AppSourceValidator.IsInstalledFromEpicGamesStore())
				Debug.Log("Use the Epic Games Store IAP SDK.");
			else if (AppSourceValidator.IsInstalledFromOfficialStore())
				Debug.Log("Use the first-party store IAP SDK (App Store / Google Play).");
			else
				Debug.Log("Store not identified yet - defer the IAP SDK choice.");

			return AppSourceValidator.GetAppSource();
		}
	}
}
