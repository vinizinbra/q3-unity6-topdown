var prevActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var prevOverride = TilesetPlatformBuilder.TilesetOverride;
var sc = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
string res = "";
try {
  var O = new UnityEngine.Vector3(6000, 0, 6000);
  var holder = new UnityEngine.GameObject("__ChunkTest"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, sc);
  foreach (var (n, x, z, yaw) in new[]{("EnemyChunk",0f,0f,0f),("EnemyChunk",24f,0f,0f),("EnemyChunk-Bridge",0f,24f,0f),("EnemyChunk-Crossroads",48f,24f,90f)}) {
    var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_QuantumUser/Entities/LevelChunk/" + n + ".prefab");
    var place = UnityEngine.Matrix4x4.TRS(O + new UnityEngine.Vector3(x, 0, z), UnityEngine.Quaternion.Euler(0, yaw, 0), UnityEngine.Vector3.one) * pf.transform.worldToLocalMatrix;
    foreach (var src in pf.GetComponentsInChildren<TilesetPlatformBuilder>(true)) {
      var box = src.GetComponent<UnityEngine.BoxCollider>(); var m = place * src.transform.localToWorldMatrix;
      var go = new UnityEngine.GameObject("cube"); go.transform.SetParent(holder.transform, false);
      go.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation); go.transform.localScale = m.lossyScale;
      var bc = go.AddComponent<UnityEngine.BoxCollider>(); bc.center = box.center; bc.size = box.size;
      go.AddComponent<TilesetPlatformBuilder>();
    }
  }
  TilesetPlatformBuilder.SetTilesetOverride(UnityEditor.AssetDatabase.LoadAssetAtPath<TilesetDefinition>("__TILESET__"), false);
  var cands = TilesetPlatformBuilder.FindCandidates(sc);
  var done = new System.Collections.Generic.HashSet<TilesetPlatformBuilder>();
  foreach (var c in holder.GetComponentsInChildren<TilesetPlatformBuilder>()) { if (done.Contains(c)) continue; c.Generate(cands); foreach (var m in c.Members) done.Add(m); }
  var target = O + new UnityEngine.Vector3(__TX__, 1, __TZ__);
  var sunGo = new UnityEngine.GameObject("__sun"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sunGo, sc);
  var sun = sunGo.AddComponent<UnityEngine.Light>(); sun.type = UnityEngine.LightType.Directional; sun.intensity = 1.2f; sun.shadows = UnityEngine.LightShadows.Soft;
  sunGo.transform.rotation = UnityEngine.Quaternion.Euler(50, -30, 0);
  var camGo = new UnityEngine.GameObject("__cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, sc);
  var cam = camGo.AddComponent<UnityEngine.Camera>(); cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor; cam.backgroundColor = __SKY__; cam.fieldOfView = 14; cam.farClipPlane = 300;
  cam.transform.SetPositionAndRotation(target + new UnityEngine.Vector3(0, 36, -36), UnityEngine.Quaternion.Euler(45, 0, 0));
  var rt = new UnityEngine.RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render();
  UnityEngine.RenderTexture.active = rt; var tex = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGB24, false); tex.ReadPixels(new UnityEngine.Rect(0,0,1280,720),0,0); tex.Apply();
  System.IO.File.WriteAllBytes("__OUT__", UnityEngine.ImageConversion.EncodeToPNG(tex));
  cam.targetTexture = null; UnityEngine.RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(rt);
  var cnt = new System.Collections.Generic.Dictionary<string,int>(); foreach (var t in holder.GetComponentsInChildren<UnityEngine.MeshFilter>(true)) { var n = t.sharedMesh ? t.sharedMesh.name : "?"; if (!n.Contains("Prop_")) continue; n = n.Substring(n.IndexOf("Prop_") + 5); cnt[n] = cnt.TryGetValue(n, out var c0) ? c0 + 1 : 1; } res = "rendered " + string.Join(" ", System.Linq.Enumerable.Select(cnt, kv => kv.Key + "=" + kv.Value));
} finally {
  TilesetPlatformBuilder.SetTilesetOverride(prevOverride, false);
  UnityEngine.SceneManagement.SceneManager.SetActiveScene(prevActive);
  UnityEditor.SceneManagement.EditorSceneManager.CloseScene(sc, true);
}
return res;
