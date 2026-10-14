using UnityEngine;
using UnityEngine.UI;

// Crops a Simple-type Image to the circle (ellipse) inscribed in its RectTransform by rebuilding its
// mesh as a triangle fan - the sprite UVs are mapped across the fan, so the icon looks exactly as if a
// circular Mask had clipped it. Replaces the "circle Image + Mask" pattern: a Mask writes/compares a
// stencil, which gives the masked children their own material instance and splits the canvas batch
// (2 masks cost ~6 SetPass and ~8 batches on the CF2 touch rig). This keeps the Image on the shared
// UI/Default material, so it batches with every other sprite on the same atlas page.
// Only the plain 4-vertex quad (Image.Type.Simple) is handled; any other mesh is left untouched.
[RequireComponent(typeof(Image))]
[AddComponentMenu("UI/Effects/Circular Image Mesh Widget")]
public class CircularImageMeshWidget : BaseMeshEffect
{
    [SerializeField, Range(12, 128), Tooltip("Ring segments. 48 stays within ~0.2 px of a true circle at 180 px; raise it for much larger icons.")]
    private int segments = 48;

#if UNITY_EDITOR
    // BaseMeshEffect only declares OnValidate in the Editor - an unguarded override breaks player builds.
    protected override void OnValidate()
    {
        base.OnValidate();
        segments = Mathf.Clamp(segments, 12, 128);
    }
#endif

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount != 4)
            return;

        var corners = new UIVertex[4];
        for (int i = 0; i < 4; i++)
            vh.PopulateUIVertex(ref corners[i], i);

        Vector2 min = corners[0].position, max = corners[0].position;
        Vector2 uvMin = corners[0].uv0, uvMax = corners[0].uv0;
        for (int i = 1; i < 4; i++)
        {
            Vector2 p = corners[i].position;
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
            uvMin = Vector2.Min(uvMin, corners[i].uv0);
            uvMax = Vector2.Max(uvMax, corners[i].uv0);
        }

        Vector2 size = max - min;
        if (size.x <= 0f || size.y <= 0f)
            return;

        Vector2 center = (min + max) * 0.5f;
        UIVertex template = corners[0];

        vh.Clear();
        vh.AddVert(MakeVertex(template, center, min, size, uvMin, uvMax));

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector2 p = center + new Vector2(Mathf.Cos(angle) * size.x * 0.5f, Mathf.Sin(angle) * size.y * 0.5f);
            vh.AddVert(MakeVertex(template, p, min, size, uvMin, uvMax));
        }

        for (int i = 0; i < segments; i++)
            vh.AddTriangle(0, 1 + i, 1 + (i + 1) % segments);
    }

    private static UIVertex MakeVertex(UIVertex template, Vector2 position, Vector2 min, Vector2 size, Vector2 uvMin, Vector2 uvMax)
    {
        UIVertex v = template;
        v.position = new Vector3(position.x, position.y, template.position.z);
        Vector2 t = new Vector2((position.x - min.x) / size.x, (position.y - min.y) / size.y);
        v.uv0 = new Vector4(Mathf.Lerp(uvMin.x, uvMax.x, t.x), Mathf.Lerp(uvMin.y, uvMax.y, t.y), 0f, 0f);
        return v;
    }
}
