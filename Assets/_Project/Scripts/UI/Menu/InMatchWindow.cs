using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Deterministic;
using Photon.Realtime;
using Quantum;
using Quantum.Demo;
using QuantumUser.View.Util;
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
        DisconnectWithReason("Game start timed out");
      }
    }

    public override void Show() {
      base.Show();
      _disconnectRequested = false;
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
      DisconnectWithReason(reason);
    }

    // Disconnects immediately instead of gating it behind an AlertPopup: this window disables the
    // menu Canvas that PopupManager lives under, so a popup raised here is invisible and unclickable
    // and the match screen would stay up forever (e.g. the Quantum plugin dropping an inactive client
    // after the app sat in the background). The reason is stashed and shown by
    // MatchMakingConfig.ReturnToMenuAfterDisconnect once the menu (and its Canvas) is back.
    private bool _disconnectRequested;

    private void DisconnectWithReason(string reason) {
      if (_disconnectRequested) return;
      _disconnectRequested = true;
      LogHelper.Warn("MatchMaking", $"InMatchWindow disconnecting: {reason}");
      MatchMakingConfig.Instance.SetPendingDisconnectReason(reason);
      MatchMakingConfig.Instance.Client?.Disconnect();
    }

    public void OnLeaveClicked() {
      // Routes through offline vs online correctly - see MatchMakingConfig.LeaveMatch's own
      // comment (reconnect information is deliberately left alone for the online case).
      MatchMakingConfig.Instance.LeaveMatch();
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
