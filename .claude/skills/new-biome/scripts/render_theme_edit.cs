// Edit-mode render of 4 test chunks with a WorldTheme's tileset, sky and water / cloud surface (placeholders:
// __THEME__ = WorldTheme asset name, __TX__ __TZ__ = camera target offset, __D__ = camera distance (36 = gameplay),
// __OUT__ = png path). Billboards face the camera; returns "rendered" + per-prop spawn counts.
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
  var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<Quantum.WorldTheme>("Assets/_Project/Data/WorldTheme/__THEME__.asset");
  UnityEngine.Material surf;
  if (theme.Water.SurfaceMaterial != null) surf = new UnityEngine.Material(theme.Water.SurfaceMaterial);
  else {
    surf = new UnityEngine.Material(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/_Project/Art/Material/Water.mat"));
    var wt = theme.Water;
    if (wt.Apply) { surf.SetColor("_ShallowColor", wt.ShallowColor); surf.SetColor("_DeepColor", wt.DeepColor); surf.SetColor("_HighlightColor", wt.GlimmerColor); surf.SetColor("_FoamColor", wt.FoamColor); if (wt.Opacity > 0) surf.SetFloat("_WaterOpacity", wt.Opacity); }
  }
  surf.DisableKeyword("_SHOREFIELD_BILLOW");
  var plane = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Quad); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(plane, sc);
  plane.transform.SetPositionAndRotation(O + new UnityEngine.Vector3(30, 0f, 20), UnityEngine.Quaternion.Euler(90, 0, 0)); plane.transform.localScale = new UnityEngine.Vector3(300, 300, 1); plane.GetComponent<UnityEngine.Renderer>().sharedMaterial = surf;
  TilesetPlatformBuilder.SetTilesetOverride(theme.Tileset.Tileset, false);
  var cands = TilesetPlatformBuilder.FindCandidates(sc);
  var done = new System.Collections.Generic.HashSet<TilesetPlatformBuilder>();
  foreach (var c in holder.GetComponentsInChildren<TilesetPlatformBuilder>()) { if (done.Contains(c)) continue; c.Generate(cands); foreach (var m in c.Members) done.Add(m); }
  var target = O + new UnityEngine.Vector3(__TX__, 1, __TZ__);
  var sunGo = new UnityEngine.GameObject("__sun"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sunGo, sc);
  var sun = sunGo.AddComponent<UnityEngine.Light>(); sun.type = UnityEngine.LightType.Directional; sun.intensity = 1.2f; sun.shadows = UnityEngine.LightShadows.Soft;
  sunGo.transform.rotation = UnityEngine.Quaternion.Euler(50, -30, 0);
  var camGo = new UnityEngine.GameObject("__cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, sc);
  var cam = camGo.AddComponent<UnityEngine.Camera>(); cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor; cam.backgroundColor = theme.Environment.Sky; cam.fieldOfView = 14; cam.farClipPlane = 300;
  cam.transform.SetPositionAndRotation(target + new UnityEngine.Vector3(0, __D__, -__D__), UnityEngine.Quaternion.Euler(45, 0, 0));
  foreach (var bb in holder.GetComponentsInChildren<Billboard>(true)) bb.transform.rotation = UnityEngine.Quaternion.LookRotation(cam.transform.forward, UnityEngine.Vector3.up);
  var rt = new UnityEngine.RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render();
  UnityEngine.RenderTexture.active = rt; var tex = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGB24, false); tex.ReadPixels(new UnityEngine.Rect(0,0,1280,720),0,0); tex.Apply();
  System.IO.File.WriteAllBytes("__OUT__", UnityEngine.ImageConversion.EncodeToPNG(tex));
  cam.targetTexture = null; UnityEngine.RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(rt);
  var cnt = new System.Collections.Generic.Dictionary<string,int>(); foreach (var t in holder.GetComponentsInChildren<UnityEngine.MeshFilter>(true)) { var n = t.sharedMesh ? t.sharedMesh.name : "?"; if (!n.Contains("Prop_")) continue; n = n.Substring(n.IndexOf("Prop_") + 5); cnt[n] = cnt.TryGetValue(n, out var c0) ? c0 + 1 : 1; }
  res = "rendered " + string.Join(" ", System.Linq.Enumerable.Select(cnt, kv => kv.Key + "=" + kv.Value));
} finally {
  TilesetPlatformBuilder.SetTilesetOverride(prevOverride, false);
  UnityEngine.SceneManagement.SceneManager.SetActiveScene(prevActive);
  UnityEditor.SceneManagement.EditorSceneManager.CloseScene(sc, true);
}
return res;
