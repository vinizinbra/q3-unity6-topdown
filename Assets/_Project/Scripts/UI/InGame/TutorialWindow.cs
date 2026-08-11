using System;
using Quantum;
using Quantum.Demo;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TutorialWindow : MonoBehaviour
{
    public TMP_Text feedbackText;
    public string[] feedbackStrings;
    public int lapIndex = 0;
    public GameObject playObject;
    public GameObject finalObject;

    private void Awake()
    {
        Application.targetFrameRate = -1;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene arg0, LoadSceneMode arg1)
    { 
        LogHelper.Log("Tutorial", $"Loaded scene {arg0.name}");
    }

    public void LoadMenu()
    {
        LogHelper.Log("Tutorial", "LOAD MENU");
        LogHelper.Log("Tutorial", "SCENES LOADED BEFORE MENU");

        LogHelper.Log("Tutorial", SceneManager.sceneCount.ToString());
        for(int i =0; i < SceneManager.sceneCount; i++)
            LogHelper.Log("Tutorial", SceneManager.GetSceneAt(i).name);
        
        SceneManager.UnloadSceneAsync("Tutorial");
        SceneManager.LoadScene("RunRaceMenu");
        //Destroy(FindObjectOfType<QuantumRunner>().gameObject);
        Destroy(FindObjectOfType<QuantumTaskRunnerJobs>().gameObject);
    }

}
