#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

#if UNITY_IOS && !UNITY_EDITOR

using System;
using System.Runtime.InteropServices;
using CodeStage.AntiCheat.Common;

namespace CodeStage.AntiCheat.Utils
{
	internal static class AppleRoutines
	{
		// Return values on error mirror the native "pending / none" sentinels (see ACTkAppleRoutines.swift).
		private const int DistributorPending = 6;
		private const int EnvironmentPending = 4;
		private const int ReceiptNone = 2;

		[DllImport("__Internal")]
		private static extern void ACTk_AppleSource_Start();

		[DllImport("__Internal")]
		private static extern int ACTk_AppleSource_IsResolved();

		[DllImport("__Internal")]
		private static extern int ACTk_AppleSource_GetDistributorKind();

		[DllImport("__Internal")]
		private static extern IntPtr ACTk_AppleSource_GetMarketplaceBundleId();

		[DllImport("__Internal")]
		private static extern int ACTk_AppleSource_GetEnvironment();

		[DllImport("__Internal")]
		private static extern int ACTk_AppleSource_GetReceiptKind();

		public static void Start()
		{
			try
			{
				ACTk_AppleSource_Start();
			}
			catch (Exception e)
			{
				ACTk.PrintExceptionForSupport($"{ACTk.LogPrefix} Couldn't start the native Apple installation source resolver!", e);
			}
		}

		public static bool IsResolved()
		{
			try
			{
				return ACTk_AppleSource_IsResolved() != 0;
			}
			catch (Exception e)
			{
				ACTk.PrintExceptionForSupport($"{ACTk.LogPrefix} Couldn't read the native Apple installation source resolution state!", e);
				return true;
			}
		}

		public static int GetDistributorKind()
		{
			try
			{
				return ACTk_AppleSource_GetDistributorKind();
			}
			catch (Exception e)
			{
				ACTk.PrintExceptionForSupport($"{ACTk.LogPrefix} Couldn't read the native app distributor kind!", e);
				return DistributorPending;
			}
		}

		public static string GetMarketplaceBundleId()
		{
			try
			{
				var pointer = ACTk_AppleSource_GetMarketplaceBundleId();
				return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
			}
			catch (Exception e)
			{
				ACTk.PrintExceptionForSupport($"{ACTk.LogPrefix} Couldn't read the native marketplace bundle id!", e);
				return string.Empty;
			}
		}

		public static int GetEnvironment()
		{
			try
			{
				return ACTk_AppleSource_GetEnvironment();
			}
			catch (Exception e)
			{
				ACTk.PrintExceptionForSupport($"{ACTk.LogPrefix} Couldn't read the native app transaction environment!", e);
				return EnvironmentPending;
			}
		}

		public static int GetReceiptKind()
		{
			try
			{
				return ACTk_AppleSource_GetReceiptKind();
			}
			catch (Exception e)
			{
				ACTk.PrintExceptionForSupport($"{ACTk.LogPrefix} Couldn't read the native receipt kind!", e);
				return ReceiptNone;
			}
		}
	}
}

#endif
