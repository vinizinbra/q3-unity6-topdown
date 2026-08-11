using Quantum;
using UnityEngine;
[DefaultExecutionOrder(-10)]
public abstract class QuantumGlobalMonoBehaviour : MonoBehaviour
{
    protected QuantumGame _game;
    private bool initialized = false;
    private void Update()
    {
        if(QuantumRunner.Default == null) return;
        if(QuantumRunner.Default.Game == null) return; 

        _game = QuantumRunner.Default.Game;
        if (!initialized)
        {
            if(_game.Frames.Verified == null) return;
            QStart(_game);
            initialized = true;
        }
        QUpdate(_game);
    }
    private void LateUpdate()
    {
        if(QuantumRunner.Default == null) return;
        if(QuantumRunner.Default.Game == null) return; 
        _game = QuantumRunner.Default.Game;
        QLateUpdate(_game);
    }
    public virtual void QStart(QuantumGame game) { }
    public abstract void QUpdate(QuantumGame game);

    public virtual void QLateUpdate(QuantumGame game) { }


}