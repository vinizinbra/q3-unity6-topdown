using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>GPU checks of the production MAX mask and shared-style composite passes.</summary>
public static class BlobShadowGpuChecks
{
    private const int Size = 64;
    private const float Tolerance = 1.5f / 255f;

    [MenuItem("Tools/Rendering/Validate Blob Shadow MAX")]
    public static void Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run blob GPU checks outside Play mode.");
        using (var fixture = new Fixture())
        {
            foreach (var pair in new[] { new Vector2(.6f, .4f), new Vector2(.1f, .8f), new Vector2(.5f, .5f), new Vector2(.2f, 0) })
            {
                fixture.A.color = new Color(1, 1, 1, pair.x);
                fixture.B.color = new Color(1, 1, 1, pair.y);
                foreach (bool reverse in new[] { false, true })
                {
                    fixture.Draw(true, true, reverse);
                    Near(fixture.ReadMask()[Size * Size / 2 + Size / 2].r, Mathf.Max(pair.x, pair.y), "MAX " + pair);
                }
            }

            // Shape alpha and SpriteRenderer height alpha combine BEFORE the reduction.
            fixture.SetShapeAlpha(.8f);
            fixture.A.color = new Color(1, 1, 1, .5f);
            fixture.B.color = new Color(1, 1, 1, .8125f);
            fixture.Draw(true, true, false);
            Near(fixture.ReadMask()[Size * Size / 2 + Size / 2].r, .65f, "height/strength fade");
            fixture.SetShapeAlpha(1);

            // Sprite RGB is deliberately ignored; alpha still selects the shared-style amount.
            fixture.A.color = new Color(1, 0, 0, .2f);
            fixture.B.color = new Color(0, 1, 0, .8f);
            foreach (bool reverse in new[] { false, true })
            {
                fixture.Draw(true, true, reverse);
                Color c = fixture.ReadComposite()[Size * Size / 2 + Size / 2];
                Near(c.r, .52f, "shared red");
                Near(c.g, .52f, "shared green");
                Near(c.b, .52f, "shared blue");
            }

            if (SystemInfo.supportsInstancing)
            {
                fixture.Draw(true, true, false, true);
                Near(fixture.ReadMask()[Size * Size / 2 + Size / 2].r, .8f, "instanced alpha");
                Color c = fixture.ReadComposite()[Size * Size / 2 + Size / 2];
                Near(c.r, .52f, "instanced shared red");
                Near(c.g, .52f, "instanced shared green");
            }

            fixture.Material.SetFloat("_ShapeSource", 0);
            fixture.Material.SetFloat("_ShapeCutout", 1);
            fixture.Material.SetFloat("_ShapeThreshold", .5f);
            fixture.SetShapeAlpha(.1f); // RGB remains white; RGB-source cutout must still be solid.
            fixture.Draw(true, true, false);
            Near(fixture.ReadMask()[Size * Size / 2 + Size / 2].r, .8f, "RGB source/cutout");
            fixture.Material.SetFloat("_ShapeSource", 1);
            fixture.Draw(true, true, false);
            Near(fixture.ReadMask()[Size * Size / 2 + Size / 2].r, 0, "alpha source/cutout");
            fixture.Material.SetFloat("_ShapeCutout", 0);

            fixture.MakeSoft();
            fixture.A.color = new Color(1, 1, 1, .6f);
            fixture.B.color = new Color(1, 1, 1, .8f);
            fixture.A.transform.position = new Vector3(-.25f, 0, 0);
            fixture.B.transform.position = new Vector3(.25f, 0, 0);
            fixture.Draw(true, false, false);
            Color[] a = fixture.ReadMask();
            Color[] colorA = fixture.ReadComposite();
            fixture.Draw(false, true, false);
            Color[] b = fixture.ReadMask();
            Color[] colorB = fixture.ReadComposite();
            foreach (bool reverse in new[] { false, true })
            {
                fixture.Draw(true, true, reverse);
                Color[] union = fixture.ReadMask();
                Color[] colorUnion = fixture.ReadComposite();
                for (int i = 0; i < union.Length; ++i)
                {
                    Near(union[i].r, Mathf.Max(a[i].r, b[i].r), "soft union pixel " + i);
                    // The shared-color field must equal the darkest SINGLE result,
                    // with neither dark accumulation nor light holes in the overlap.
                    Near(colorUnion[i].r, Mathf.Min(colorA[i].r, colorB[i].r), "soft composite pixel " + i);
                }
            }

            // Sliced geometry uses the same UV-based shape sampling (no radial/procedural shape).
            fixture.A.drawMode = SpriteDrawMode.Sliced;
            fixture.A.size = new Vector2(1.8f, 1.2f);
            fixture.Draw(true, false, false);
            a = fixture.ReadMask();
            fixture.Draw(false, true, false);
            b = fixture.ReadMask();
            fixture.Draw(true, true, false);
            Color[] sliced = fixture.ReadMask();
            for (int i = 0; i < sliced.Length; ++i)
                Near(sliced[i].r, Mathf.Max(a[i].r, b[i].r), "sliced union pixel " + i);

            // Hatch is evaluated after MAX. It must not change the amount target.
            fixture.Material.SetTexture("_HatchMap", Texture2D.blackTexture);
            fixture.Material.SetFloat("_HatchStrength", .5f);
            fixture.Draw(true, true, false);
            Color[] hatched = fixture.ReadMask();
            Color[] hatchedColor = fixture.ReadComposite();
            for (int i = 0; i < sliced.Length; ++i)
            {
                Near(hatched[i].r, sliced[i].r, "hatch changed raw amount " + i);
                float expected = Mathf.Lerp(1, .4f, sliced[i].r) * Mathf.Lerp(1, 0, sliced[i].r * .5f);
                Near(hatchedColor[i].r, expected, "shared hatch composite " + i);
            }

            fixture.Draw(false, false, false);
            Near(fixture.ReadMask()[Size * Size / 2].r, 0, "mask clears each frame");
            Near(fixture.ReadComposite()[Size * Size / 2].r, 1, "empty composite is neutral");
        }
        Debug.Log("Blob shadow GPU checks passed: all four MAX cases, both orders, height fade, shared RGB, GPU instancing, shape controls, soft gradients, slicing, hatch isolation, and clearing.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void Near(float actual, float expected, string label)
    {
        if (Mathf.Abs(actual - expected) > Tolerance)
            throw new Exception($"Blob shadow {label}: expected {expected}, got {actual}");
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Material Material;
        public readonly SpriteRenderer A;
        public readonly SpriteRenderer B;
        private readonly Material composite;
        private readonly RenderTexture mask, output;
        private readonly Texture2D shape;
        private readonly Sprite sprite;
        private readonly Mesh mesh;
        private readonly Texture oldDepth;
        private readonly Matrix4x4 oldInverseVP;
        private readonly Texture oldBlit;
        private readonly Vector4 oldSize, oldDepthSize, oldBlitScale;

        public Fixture()
        {
            Shader shader = Shader.Find("Custom/BlobShadowSpriteMultiply");
            Shader compositeShader = Shader.Find("Hidden/BlobShadowComposite");
            if (shader == null || compositeShader == null || ShaderUtil.ShaderHasError(shader) || ShaderUtil.ShaderHasError(compositeShader))
                throw new Exception("Blob shaders missing or have compilation errors.");
            Material = new Material(shader) { enableInstancing = true };
            Material.SetFloat("_ShapeSource", 1);
            Material.SetFloat("_MinShadowAmount", 0);
            Material.SetColor("_ShadowColor", new Color(.4f, .4f, .4f, 1));
            composite = new Material(compositeShader);
            shape = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            var pixels = new Color[Size * Size];
            Array.Fill(pixels, Color.white);
            shape.SetPixels(pixels);
            shape.Apply();
            sprite = Sprite.Create(shape, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), Size / 2f,
                0, SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
            mesh = new Mesh
            {
                vertices = Array.ConvertAll(sprite.vertices, v => new Vector3(v.x, v.y, 0)),
                uv = sprite.uv,
                triangles = Array.ConvertAll(sprite.triangles, i => (int)i),
                colors = new[] { Color.white, Color.white, Color.white, Color.white }
            };
            A = MakeRenderer("Blob GPU A");
            B = MakeRenderer("Blob GPU B");
            mask = MakeTarget(BlobShadowRendererFeature.SelectMaskFormat());
            output = MakeTarget(GraphicsFormat.R8G8B8A8_UNorm);
            oldDepth = Shader.GetGlobalTexture("_CameraDepthTexture");
            oldInverseVP = Shader.GetGlobalMatrix("unity_MatrixInvVP");
            oldSize = Shader.GetGlobalVector("_BlobShadowMaskSize");
            oldDepthSize = Shader.GetGlobalVector("_CameraDepthTexture_TexelSize");
            oldBlit = Shader.GetGlobalTexture("_BlitTexture");
            oldBlitScale = Shader.GetGlobalVector("_BlitScaleBias");
        }

        private SpriteRenderer MakeRenderer(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = Material;
            return renderer;
        }

        private static RenderTexture MakeTarget(GraphicsFormat format)
        {
            if (format == GraphicsFormat.None) throw new Exception("No blendable shadow mask format.");
            var rt = new RenderTexture(new RenderTextureDescriptor(Size, Size) { graphicsFormat = format, depthBufferBits = 0, msaaSamples = 1 });
            rt.Create();
            return rt;
        }

        public void MakeSoft()
        {
            for (int y = 0; y < Size; ++y)
            for (int x = 0; x < Size; ++x)
            {
                float radius = new Vector2((x + .5f) / Size * 2 - 1, (y + .5f) / Size * 2 - 1).magnitude;
                shape.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(1, 0, radius)));
            }
            shape.Apply();
        }

        public void SetShapeAlpha(float alpha)
        {
            var pixels = new Color[Size * Size];
            Array.Fill(pixels, new Color(1, 1, 1, alpha));
            shape.SetPixels(pixels);
            shape.Apply();
        }

        public void Draw(bool drawA, bool drawB, bool reverse, bool instanced = false)
        {
            composite.SetColor("_ShadowColor", Material.GetColor("_ShadowColor"));
            composite.SetTexture("_HatchMap", Material.GetTexture("_HatchMap"));
            composite.SetFloat("_HatchScale", Material.GetFloat("_HatchScale"));
            composite.SetFloat("_HatchStrength", Material.GetFloat("_HatchStrength"));
            using (var cmd = new CommandBuffer { name = "Blob shadow GPU regression" })
            {
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.SetGlobalMatrix("unity_MatrixInvVP", Matrix4x4.identity);
                cmd.SetGlobalTexture("_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? Texture2D.blackTexture : Texture2D.whiteTexture);
                cmd.SetGlobalVector("_CameraDepthTexture_TexelSize", new Vector4(.25f, .25f, 4, 4));
                cmd.SetGlobalVector("_BlobShadowMaskSize", new Vector4(Size, Size, 1f / Size, 1f / Size));
                cmd.SetRenderTarget(mask);
                cmd.ClearRenderTarget(false, true, Color.clear);
                DrawPair(cmd, 0, drawA, drawB, reverse, instanced);
                cmd.SetRenderTarget(output);
                cmd.ClearRenderTarget(false, true, Color.white);
                cmd.SetGlobalTexture("_BlitTexture", mask);
                cmd.SetGlobalVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                cmd.DrawProcedural(Matrix4x4.identity, composite, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(cmd);
            }
        }

        private void DrawPair(CommandBuffer cmd, int pass, bool drawA, bool drawB, bool reverse, bool instanced)
        {
            if (instanced)
            {
                var properties = new MaterialPropertyBlock();
                properties.SetVectorArray("unity_SpriteRendererColorArray", new Vector4[] { A.color, B.color });
                properties.SetTexture("_MainTex", shape);
                cmd.DrawMeshInstanced(mesh, 0, Material, pass, new[] { A.localToWorldMatrix, B.localToWorldMatrix }, 2, properties);
                return;
            }
            if (reverse && drawB) cmd.DrawRenderer(B, Material, 0, pass);
            if (drawA) cmd.DrawRenderer(A, Material, 0, pass);
            if (!reverse && drawB) cmd.DrawRenderer(B, Material, 0, pass);
        }

        public Color[] ReadMask() => Read(mask);
        public Color[] ReadComposite() => Read(output);

        private static Color[] Read(RenderTexture target)
        {
            var old = RenderTexture.active;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                texture.Apply();
                return texture.GetPixels();
            }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(texture); }
        }

        public void Dispose()
        {
            Shader.SetGlobalTexture("_CameraDepthTexture", oldDepth);
            Shader.SetGlobalMatrix("unity_MatrixInvVP", oldInverseVP);
            Shader.SetGlobalVector("_BlobShadowMaskSize", oldSize);
            Shader.SetGlobalVector("_CameraDepthTexture_TexelSize", oldDepthSize);
            Shader.SetGlobalTexture("_BlitTexture", oldBlit);
            Shader.SetGlobalVector("_BlitScaleBias", oldBlitScale);
            UnityEngine.Object.DestroyImmediate(A.gameObject);
            UnityEngine.Object.DestroyImmediate(B.gameObject);
            foreach (var obj in new UnityEngine.Object[] { Material, composite, sprite, mesh, shape, mask, output })
                UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
