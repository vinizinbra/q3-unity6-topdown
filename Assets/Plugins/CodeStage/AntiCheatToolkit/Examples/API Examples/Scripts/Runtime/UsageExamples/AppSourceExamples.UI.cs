#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

using CodeStage.AntiCheat.Genuine;

namespace CodeStage.AntiCheat.Examples
{
	using UnityEngine;

	internal partial class AppSourceExamples
	{
		public void DrawUI()
		{
			GUILayout.Label("<b>App Installation Source</b>");
			GUILayout.Space(10);
			using (new GUILayout.VerticalScope(GUI.skin.box))
			{
				GUILayout.Label(
					"AppSourceValidator reports which store delivered the app, with one cross-platform API for Android and iOS.\n" +
					"Use it to pick an In-App Purchase SDK or to flag installs from outside your trusted stores.");
				GUILayout.Space(5);

				if (Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer)
				{
					GUILayout.Label(ExamplesGUI.Colorize("Detection runs in Android and iOS builds.", ExamplesGUI.YellowColor));
					return;
				}

				if (!AppSourceValidator.IsResolved)
					GUILayout.Label(ExamplesGUI.Colorize("iOS source is still resolving - try again in a moment.", ExamplesGUI.YellowColor));

				if (AppSourceResult != null)
				{
					GUILayout.Label($"Platform: {AppSourceResult.Platform}");
					GUILayout.Label($"Official store: {AppSourceResult.IsOfficialStore}");
					if (AppSourceResult.Android != null)
						GUILayout.Label($"Android store: {AppSourceResult.Android}");
					if (AppSourceResult.Apple != null)
						GUILayout.Label($"Apple store: {AppSourceResult.Apple}");
					if (!string.IsNullOrEmpty(AppSourceResult.RawSourceId))
						GUILayout.Label($"Source id: {AppSourceResult.RawSourceId}");
				}

				if (GUILayout.Button("Get App Source"))
					AppSourceResult = GetInstallationSource();
			}
		}
	}
}
