using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
 
public class FpsCounter : MonoBehaviour
{
	public TMP_Text Text;
 
	private Dictionary<int, string> CachedNumberStrings = new();
	private int[] _frameRateSamples;
	private int _cacheNumbersAmount = 300;
	private int _averageFromAmount = 30;
	private int _averageCounter = 0;
	private int _currentAveraged;
 
	void Awake()
	{
		// Cache strings and create array
		{
			for (int i = 0; i < _cacheNumbersAmount; i++) {
				CachedNumberStrings[i] = i.ToString();
			}
			_frameRateSamples = new int[_averageFromAmount];
		}
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
			
			Text.text = $"FPS "+_currentAveraged switch
			{
				var x when x >= 0 && x < _cacheNumbersAmount => CachedNumberStrings[x],
				var x when x >= _cacheNumbersAmount => $"> {_cacheNumbersAmount}",
				var x when x < 0 => "< 0",
				_ => "?"
			};
			
		}
	}
}
