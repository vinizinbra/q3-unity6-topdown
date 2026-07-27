#region copyright
// -------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// -------------------------------------------------------
#endregion

namespace CodeStage.AntiCheat.Storage
{
	using Common;

	internal class DeviceUniqueIdentifierException : BackgroundThreadAccessException
	{
		public DeviceUniqueIdentifierException() : base("SystemInfo.deviceUniqueIdentifier")
		{
		}
	}
}
