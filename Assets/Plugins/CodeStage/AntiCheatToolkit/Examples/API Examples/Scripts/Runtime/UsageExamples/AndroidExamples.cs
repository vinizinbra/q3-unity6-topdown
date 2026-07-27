#region copyright
// ------------------------------------------------------
// Copyright (C) Dmitry Yuhanov [https://codestage.net]
// ------------------------------------------------------
#endregion

using UnityEngine;
using CodeStage.AntiCheat.Storage;
using CodeStage.AntiCheat.Utils;

namespace CodeStage.AntiCheat.Examples
{
	internal partial class AndroidExamples : MonoBehaviour
	{
		private bool IsScreenRecordingAllowed { get; set; } = true;

		private void Awake()
		{
			if (!Application.isEditor)
			{
				IsScreenRecordingAllowed = ObscuredPrefs.Get(nameof(IsScreenRecordingAllowed), true);
				ApplyIsScreenRecordingAllowed();
			}
		}

		private void SwitchScreenRecording()
		{
			IsScreenRecordingAllowed = !IsScreenRecordingAllowed;
			ObscuredPrefs.Set(nameof(IsScreenRecordingAllowed), IsScreenRecordingAllowed);
			ApplyIsScreenRecordingAllowed();
		}

		private void ApplyIsScreenRecordingAllowed()
		{
			if (IsScreenRecordingAllowed)
				AndroidScreenRecordingBlocker.AllowScreenRecording();
			else
				AndroidScreenRecordingBlocker.PreventScreenRecording();
		}
	}
}
