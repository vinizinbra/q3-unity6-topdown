using UnityEditor;
using UnityEngine;

// Scripted authoring of ParticleSound for builders that generate effect prefabs (boss builders): one call instead of
// the GetComponent/SerializedObject dance, since ParticleSound keeps its fields private.
internal static class ParticleSoundAuthoring
{
    // A OneShot ParticleSound on `root` (added if missing) playing `sound` each time the effect starts.
    internal static void SetOneShot(GameObject root, SoundData sound)
    {
        ParticleSound particleSound = root.GetComponent<ParticleSound>();
        if (particleSound == null)
            particleSound = root.AddComponent<ParticleSound>();

        var so = new SerializedObject(particleSound);
        so.FindProperty("mode").enumValueIndex = (int)ParticleSound.Mode.OneShot;
        so.FindProperty("sound").objectReferenceValue = sound;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
