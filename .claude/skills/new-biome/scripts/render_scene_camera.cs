UnityEngine.Camera cam = null; foreach (var c in UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None)) if (c.name == "Main Camera") cam = c;
var rt = new UnityEngine.RenderTexture(1280, 720, 24); var prev = cam.targetTexture; cam.targetTexture = rt; cam.Render();
UnityEngine.RenderTexture.active = rt; var tex = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGB24, false); tex.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
System.IO.File.WriteAllBytes("__OUT__", UnityEngine.ImageConversion.EncodeToPNG(tex));
cam.targetTexture = prev; UnityEngine.RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(rt);
return "ok";
