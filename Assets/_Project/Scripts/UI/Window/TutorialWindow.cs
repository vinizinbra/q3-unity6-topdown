using System;
using Quantum;
using Quantum.Demo;
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
        Debug.Log($"Loaded scene {arg0.name}");
    }

    public void LoadMenu()
    {
        Debug.Log("LOAD MENU");
        Debug.Log("SCENES LOADED BEFORE MENU");

        Debug.Log(SceneManager.sceneCount);
        for(int i =0; i < SceneManager.sceneCount; i++)
            Debug.Log(SceneManager.GetSceneAt(i).name);
        
        SceneManager.UnloadSceneAsync("Tutorial");
        SceneManager.LoadScene("RunRaceMenu");
        //Destroy(FindObjectOfType<QuantumRunner>().gameObject);
        Destroy(FindObjectOfType<QuantumTaskRunnerJobs>().gameObject);
    }

}
