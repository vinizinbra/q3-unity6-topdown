#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

#if UNITY_IOS

using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace CodeStage.AntiCheat.EditorCode.PostProcessors
{
	/// <summary>
	/// Links the frameworks the Apple installation source detector needs into the generated Xcode project.
	/// StoreKit backs AppTransaction (iOS 16+). MarketplaceKit backs AppDistributor (iOS 17.4+) and is
	/// weak-linked so the app still launches on older iOS versions where the framework is absent. MarketplaceKit
	/// is added only when the build SDK actually contains it (Xcode 15.3+ / iOS 17.4 SDK), matching the Swift's
	/// #if canImport(MarketplaceKit) guard, so building with an older SDK doesn't fail with "framework not found".
	/// </summary>
	/// <remarks>
	/// Requires the iOS Build Support module (provides UnityEditor.iOS.Xcode); compiled only when the active
	/// build target is iOS.
	/// </remarks>
	internal class AppleSourceBuildPostprocessor : IPostprocessBuildWithReport
	{
		int IOrderedCallback.callbackOrder => 100;

		public void OnPostprocessBuild(BuildReport report)
		{
			if (report.summary.platform != UnityEditor.BuildTarget.iOS)
				return;

			var projectPath = PBXProject.GetPBXProjectPath(report.summary.outputPath);

			var project = new PBXProject();
			project.ReadFromFile(projectPath);

			var frameworkTargetGuid = project.GetUnityFrameworkTargetGuid();

			project.AddFrameworkToProject(frameworkTargetGuid, "StoreKit.framework", false);
			if (IsMarketplaceKitInSdk())
				project.AddFrameworkToProject(frameworkTargetGuid, "MarketplaceKit.framework", true);

			project.WriteToFile(projectPath);
		}

		private static bool IsMarketplaceKitInSdk()
		{
#if UNITY_EDITOR_OSX
			// On macOS we can inspect the active iOS SDK. MarketplaceKit ships in the iOS 17.4 SDK (Xcode 15.3+).
			try
			{
				var startInfo = new System.Diagnostics.ProcessStartInfo("xcrun", "--sdk iphoneos --show-sdk-path")
				{
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};
				using (var process = System.Diagnostics.Process.Start(startInfo))
				{
					var sdkPath = process.StandardOutput.ReadToEnd().Trim();
					process.WaitForExit();
					if (!string.IsNullOrEmpty(sdkPath))
						return System.IO.Directory.Exists(System.IO.Path.Combine(sdkPath, "System/Library/Frameworks/MarketplaceKit.framework"));
				}
			}
			catch
			{
				// Couldn't probe the SDK; fall through and assume a modern toolchain.
			}
			return true;
#else
			// Non-macOS build hosts (e.g. Windows generating the Xcode project) can't inspect the Mac's SDK, so
			// assume a modern toolchain (Xcode 15.3+ / iOS 17.4 SDK) where MarketplaceKit exists.
			return true;
#endif
		}
	}
}

#endif
