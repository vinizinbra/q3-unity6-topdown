using System;
using Photon.Realtime;
using UnityEngine;

  public class PhotonMain : MonoBehaviour {
   // public static RealtimeClient Client { get; set; }
    public static Action OnDisconnect;

    public enum PhotonEventCode : byte {
      StartGame = 110,
      WaitingForPlayers = 111,
      SyncTime = 112,
    }

    private void Update() 
    {
      //Client?.Service();
    }
    
    public static void Disconnect()
    {
      /*if(GameManager.Instance.Unwrap().isPlayingOffline)
        GameManager.Instance.Unwrap().UnloadScene();*/
      OnDisconnect?.Invoke();
      //Client?.Disconnect();
      //Client = null;
    }
  }
