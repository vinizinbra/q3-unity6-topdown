using NaughtyAttributes;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

// Bakes every ChunkWallCube under this chunk into the QuantumEntityPrototype's compound
// PhysicsCollider - lets a chunk's solid shape be authored as plain cubes in the Scene view
// instead of hand-tuning a single collider or Shape3DConfig fields directly.
[RequireComponent(typeof(QuantumEntityPrototype))]
public class ChunkCompoundColliderBuilder : MonoBehaviour
{
    [Button]
    public void RebuildCompoundCollider()
    {
        ChunkWallCube[] wallCubes = GetComponentsInChildren<ChunkWallCube>();
        if (wallCubes.Length == 0)
        {
            LogHelper.Warn("ChunkCompoundColliderBuilder", $"No ChunkWallCube found under {name} - leaving collider untouched.", this);
            return;
        }

        var shapes = new Shape3DConfig.CompoundShapeData3D[wallCubes.Length];
        for (int i = 0; i < wallCubes.Length; i++)
        {
            shapes[i] = BuildBoxShape(wallCubes[i].GetComponent<BoxCollider>());
        }

        QuantumEntityPrototype prototype = GetComponent<QuantumEntityPrototype>();
        prototype.PhysicsCollider.IsEnabled = true;
        prototype.PhysicsCollider.Shape3D = new Shape3DConfig
        {
            ShapeType = Shape3DType.Compound,
            IsPersistent = true,
            CompoundShapes = shapes,
        };

        LogHelper.Log("ChunkCompoundColliderBuilder", $"Rebuilt compound collider on {name} from {wallCubes.Length} cube(s).", this);
    }

    private Shape3DConfig.CompoundShapeData3D BuildBoxShape(BoxCollider cube)
    {
        Vector3 worldCenter = cube.transform.TransformPoint(cube.center);
        Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
        Vector3 halfExtents = Vector3.Scale(cube.size, cube.transform.lossyScale) * 0.5f;

        return new Shape3DConfig.CompoundShapeData3D(new Shape3DConfig
        {
            ShapeType = Shape3DType.Box,
            BoxExtents = halfExtents.ToFPVector3(),
            PositionOffset = localCenter.ToFPVector3(),
        });
    }
}
