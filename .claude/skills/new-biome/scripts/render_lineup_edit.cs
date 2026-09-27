var prevActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var sc = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
string res = "";
try {
  var dir = "Assets/Test/Environment/Tileset/__SET__/";
  var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(dir + "__SET___Toon.mat");
  var O = new UnityEngine.Vector3(7000, 0, 7000);
  var wall = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, sc);
  wall.transform.position = O + new UnityEngine.Vector3(2.5f, 0.5f, 0.5f); wall.transform.localScale = new UnityEngine.Vector3(7, 3, 1); wall.GetComponent<UnityEngine.Renderer>().sharedMaterial = mat;
  var floor = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor, sc);
  floor.transform.position = O + new UnityEngine.Vector3(2.5f, -0.5f, -1.5f); floor.transform.localScale = new UnityEngine.Vector3(7, 1, 3); floor.GetComponent<UnityEngine.Renderer>().sharedMaterial = mat;
  var names = new[]{__NAMES__};
  for (int i = 0; i < names.Length; i++) {
    var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(dir + "Models/__PREFIX__" + names[i] + ".fbx");
    var go = UnityEngine.Object.Instantiate(pf); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, sc);
    bool foot = System.Array.IndexOf(new[]{__FOOT__}, names[i]) >= 0;
    go.transform.position = O + new UnityEngine.Vector3(i * 1.0f, foot ? 0f : 0.35f, 0f);
    foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>()) if (!r.sharedMaterial.name.Contains("Graffiti")) r.sharedMaterial = mat;
  }
  var sunGo = new UnityEngine.GameObject("__sun"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sunGo, sc);
  var sun = sunGo.AddComponent<UnityEngine.Light>(); sun.type = UnityEngine.LightType.Directional; sun.intensity = 1.2f; sun.shadows = UnityEngine.LightShadows.Soft;
  sunGo.transform.rotation = UnityEngine.Quaternion.Euler(50, -30, 0);
  var camGo = new UnityEngine.GameObject("__cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, sc);
  var cam = camGo.AddComponent<UnityEngine.Camera>(); cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor; cam.backgroundColor = new UnityEngine.Color(0.5f,0.7f,0.8f); cam.fieldOfView = 14; cam.farClipPlane = 300;
  var target = O + new UnityEngine.Vector3((names.Length - 1) * 0.5f, 0.3f, -0.2f);
  cam.transform.SetPositionAndRotation(target + new UnityEngine.Vector3(0, __D__, -__D__), UnityEngine.Quaternion.Euler(45, 0, 0));
  var rt = new UnityEngine.RenderTexture(1600, 600, 24); cam.targetTexture = rt; cam.Render();
  UnityEngine.RenderTexture.active = rt; var tex = new UnityEngine.Texture2D(1600, 600, UnityEngine.TextureFormat.RGB24, false); tex.ReadPixels(new UnityEngine.Rect(0,0,1600,600),0,0); tex.Apply();
  System.IO.File.WriteAllBytes("__OUT__", UnityEngine.ImageConversion.EncodeToPNG(tex));
  cam.targetTexture = null; UnityEngine.RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(rt);
  res = "rendered";
} catch (System.Exception ex) { res = ex.ToString(); }
finally { UnityEngine.SceneManagement.SceneManager.SetActiveScene(prevActive); UnityEditor.SceneManagement.EditorSceneManager.CloseScene(sc, true); }
return res;
