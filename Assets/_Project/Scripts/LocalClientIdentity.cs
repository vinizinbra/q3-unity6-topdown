using System;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Identifies THIS running client instance apart from any other instance sharing the same machine.
///
/// Unity's Multiplayer Play Mode runs each virtual player as its own Editor process, but PlayerPrefs
/// is keyed by companyName/productName alone - so every virtual player reads and writes the SAME
/// store. Any locally-persisted match identity (the Photon UserId, the reconnect information) would
/// therefore be shared between them: Photon sees one user trying to occupy the room twice
/// (JoinFailedFoundActiveJoiner), and a reconnect can never target the right actor because both
/// instances claim the same one.
///
/// A virtual player's project root lives under Library/VP/mppm&lt;id&gt; with Assets symlinked back to
/// the real project, so that id is a stable, dependency-free discriminator - stable across restarts
/// of that same virtual player, which is exactly what reconnect needs. Reading it off the path
/// rather than asking the MPPM package is deliberate: com.unity.multiplayer.playmode 2.x ships no
/// runtime assembly to reference.
///
/// Empty for the main Editor and for every real build, so nothing about shipped behaviour changes
/// and existing PlayerPrefs keys keep their original names.
/// </summary>
public static class LocalClientIdentity
{
   private const string VirtualPlayerMarker = "/Library/VP/";

   /// <summary>
   /// Stable id of this Multiplayer Play Mode virtual player (e.g. "mppm26a4d94e"), or an empty
   /// string in the main Editor and in builds.
   /// </summary>
   public static string InstanceId { get; } = ResolveInstanceId();

   /// <summary>
   /// Suffix to append to any PlayerPrefs key that must not be shared between local instances.
   /// Empty (i.e. the key is unchanged) outside Multiplayer Play Mode.
   /// </summary>
   public static string PrefSuffix => string.IsNullOrEmpty(InstanceId) ? string.Empty : "_" + InstanceId;

   private static string ResolveInstanceId()
   {
#if UNITY_EDITOR
      string path = Application.dataPath.Replace('\\', '/');
      int markerIndex = path.IndexOf(VirtualPlayerMarker, StringComparison.Ordinal);
      if (markerIndex < 0)
         return string.Empty;

      int start = markerIndex + VirtualPlayerMarker.Length;
      int end = path.IndexOf('/', start);
      return end < 0 ? path.Substring(start) : path.Substring(start, end - start);
#else
      return string.Empty;
#endif
   }
}

/// <summary>
/// Drop-in replacement for Quantum's own <see cref="Quantum.QuantumReconnectInformation"/> that
/// stores under a per-instance PlayerPrefs key (see <see cref="LocalClientIdentity"/>) and flushes
/// to disk immediately.
///
/// The SDK version hardcodes "Quantum.ReconnectInformation", so two Multiplayer Play Mode virtual
/// players overwrite each other's reconnect data - whoever joined last wins and the other can never
/// rejoin its own actor. It also only calls PlayerPrefs.SetString, which Unity does not flush until
/// OnApplicationQuit, so a crash or a killed process - precisely what a reconnect is for - loses
/// everything written that session.
///
/// In the main Editor and in builds the key is identical to the SDK's, so previously-saved
/// information is still picked up.
/// </summary>
public class LocalReconnectInformation : MatchmakingReconnectInformation
{
   private const string BaseKey = "Quantum.ReconnectInformation";

   private static string Key => BaseKey + LocalClientIdentity.PrefSuffix;

   /// <summary>Always returns a valid object, same contract as the SDK's own Load.</summary>
   public static MatchmakingReconnectInformation Load()
   {
      return JsonUtility.FromJson<LocalReconnectInformation>(PlayerPrefs.GetString(Key))
             ?? new LocalReconnectInformation();
   }

   public override void Set(RealtimeClient client)
   {
      base.Set(client);

      if (client != null)
         Save(this);
   }

   public static void Reset()
   {
      PlayerPrefs.SetString(Key, string.Empty);
      PlayerPrefs.Save();
   }

   public static void Save(LocalReconnectInformation value)
   {
      PlayerPrefs.SetString(Key, JsonUtility.ToJson(value));
      PlayerPrefs.Save();
   }
}
