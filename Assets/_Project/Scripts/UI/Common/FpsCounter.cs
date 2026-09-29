using System;
using Quantum;
using TMPro;
using Unity.Profiling;
using UnityEngine;

public class FpsCounter : MonoBehaviour
{
	public TMP_Text Text;

	private const int MaxDisplayedFps = 300;

	private int[] _frameRateSamples;
	private int _averageFromAmount = 30;
	private int _averageCounter = 0;
	private int _currentAveraged;

	// Unity's own render counters (the same ones the Profiler's Rendering module shows), read at
	// runtime - lets you see on device whether an area that drops frames is also heavier to draw.
	// Last frame's values; a recorder whose counter isn't available on this player stays !Valid and
	// its line is simply left out.
	private ProfilerRecorder _setPassRecorder;
	private ProfilerRecorder _trianglesRecorder;

	void Awake()
	{
		_frameRateSamples = new int[_averageFromAmount];

		// The label was sized for "FPS 60" alone (200 wide, top-right anchored) - the stats line is
		// drawn smaller (see Update) and must not wrap, or it breaks onto a third line / off-screen.
		Text.textWrappingMode = TextWrappingModes.NoWrap;
	}

	void OnEnable()
	{
		_setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
		_trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
	}

	void OnDisable()
	{
		_setPassRecorder.Dispose();
		_trianglesRecorder.Dispose();
	}

	void Update()
	{
		// Sample
		{
			var currentFrame = (int)Math.Round(1f / Time.smoothDeltaTime); // If your game modifies Time.timeScale, use unscaledDeltaTime and smooth manually (or not).
			_frameRateSamples[_averageCounter] = currentFrame;
		}

		// Average
		{
			var average = 0f;

			foreach (var frameRate in _frameRateSamples) {
				average += frameRate;
			}

			_currentAveraged = (int)Math.Round(average / _averageFromAmount);
			_averageCounter = (_averageCounter + 1) % _averageFromAmount;
		}

		// Assign to UI
		{
			switch (_currentAveraged)
			{
				case var x when x >= 60:
					Text.color = Color.green;
					break;
				case var x when x >= 30:
					Text.color = Color.yellow;
					break;
				default:
					Text.color = Color.red;
					break;
			}

			int fps = Mathf.Clamp(_currentAveraged, 0, MaxDisplayedFps);

			// Round-trip ping of the running Quantum session (same source as Photon's own QuantumStats) -
			// only while a match is actually online; offline/Practice and the menu have no ping to show.
			var session = QuantumRunner.Default != null ? QuantumRunner.Default.Game?.Session : null;
			bool online = session != null && session.IsOnline;
			int ping = online ? session.Stats.Ping : 0;

			// SetText(format, values) formats without allocating a string every frame.
			bool stats = _setPassRecorder.Valid && _trianglesRecorder.Valid;
			if (stats && online)
				Text.SetText("FPS {0}\n<size=55%>SP {1}  Tris {2:1}k\nPing {3}ms</size>", fps, _setPassRecorder.LastValue, _trianglesRecorder.LastValue / 1000f, ping);
			else if (stats)
				Text.SetText("FPS {0}\n<size=55%>SP {1}  Tris {2:1}k</size>", fps, _setPassRecorder.LastValue, _trianglesRecorder.LastValue / 1000f);
			else if (online)
				Text.SetText("FPS {0}\n<size=55%>Ping {1}ms</size>", fps, ping);
			else
				Text.SetText("FPS {0}", fps);
		}
	}
}
