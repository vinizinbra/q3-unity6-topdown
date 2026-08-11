using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Deterministic;
using Photon.Realtime;
using Quantum;
using Quantum.Demo;
using UnityEngine;

public class InMatchWindow : UiWindow
{
    public static InMatchWindow instance;
    public Canvas canvas;
    private void Awake()
    {
      instance = this;
    }

    public byte[] FrameSnapshot {
      get {
        if (Mathf.RoundToInt(Time.time) < _frameSnapshotTimeout) {
          return _frameSnapshot;
        }
        return null;
      }
    }

    public int FrameSnapshotNumber {
      get {
        if (Mathf.RoundToInt(Time.time) < _frameSnapshotTimeout) {
          return _frameSnapshotNumber;
        }
        return 0;
      }
    }

    private byte[] _frameSnapshot;
    private int _frameSnapshotNumber;
    private float _frameSnapshotTimeout;

    public void Update() {
      
      if (QuantumRunner.Default != null && QuantumRunner.Default.HasGameStartTimedOut) {
        AlertPopup.Show("Error", "Game start timed out", () => {
          MatchMakingConfig.Instance.Client.Disconnect();
        });
      }
    }

    public override void Show() {
      base.Show();
      _frameSnapshot = null;
      _frameSnapshotNumber = 0;
      _frameSnapshotTimeout = 0.0f;
      canvas.enabled = false;
      MatchMakingConfig.Instance.Client?.AddCallbackTarget(this);
      QuantumCallback.Subscribe(this, (CallbackPluginDisconnect c) => OnCallbackPluginDisconnect(c.Reason));

     
    }

    public override void Hide() {
      base.Hide();
      canvas.enabled = true;
      QuantumCallback.UnsubscribeListener(this);
      MatchMakingConfig.Instance.Client?.RemoveCallbackTarget(this);
      
    }

    private void OnCallbackPluginDisconnect(string reason) {
      AlertPopup.Show("Plugin Disconnect", reason, () => {
        MatchMakingConfig.Instance.Client.Disconnect();
      });
    }

    public void OnLeaveClicked() {
      MatchMakingConfig.Instance.Client.Disconnect();
    }

    public void OnConnected() {
    }

    public void OnConnectedToMaster() {
    }

    public void OnRegionListReceived(RegionHandler regionHandler) {
    }

    public void OnCustomAuthenticationResponse(Dictionary<string, object> data) {
    }

    public void OnCustomAuthenticationFailed(string debugMessage) {
    }
}
