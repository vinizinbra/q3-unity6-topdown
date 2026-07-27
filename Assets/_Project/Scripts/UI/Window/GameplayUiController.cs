using System;
using Quantum;
using TMPro;
using UnityEngine;

public class GameplayUiController : QuantumGlobalMonoBehaviour
{
    [SerializeField] private WindowManager windowManager;
    [SerializeField] private TMP_Text lives;
    [SerializeField] private TMP_Text rtt;
    public bool isDead = false;
    private int _placement;
    private Action<int> _onLeave;

    private void Start()
    {
        windowManager.ShowWindow<LoadingWindow>();

    }

    void LoadWaitingWindow(EntityRef entityRef)
    {
        windowManager.ShowWindow<WaitingWindow>();
    }
    
    public void Leave()
    {
        PhotonMain.Disconnect();
        _onLeave?.Invoke(_placement);
        
    }

    public override void QStart(QuantumGame game)
    {

    }

    public override void QUpdate(QuantumGame game)
    {

    }

    public override void QLateUpdate(QuantumGame game)
    {

    }

}