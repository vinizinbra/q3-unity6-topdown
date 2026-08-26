using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.Audio.EditorTools
{
    // Gives every NEWLY added AudioClip the same import settings AudioImportOptimizer applies in
    // bulk, so the "LoadFMODSound" main-thread stalls those settings exist to prevent cannot creep
    // back in one dragged-in .wav at a time.
    //
    // Deliberately only touches clips on their FIRST import (importSettingsMissing - no .meta file
    // yet). Anything already in the project keeps whatever a human, or the bulk tool, decided:
    // re-deciding on every reimport would silently overwrite a hand-tuned clip, and would also fight
    // the bulk tool's own reimport.
    //
    // That flag is also what makes the SaveAndReimport below safe. Unity writes the .meta as part
    // of this first import, so the reimport this triggers sees importSettingsMissing == false and
    // stops - there is no loop.
    internal sealed class AudioImportDefaults : AssetPostprocessor
    {
        // Length is the input the rules are built around, and it is only knowable once the clip
        // has actually been decoded - which is why this is OnPostprocessAudioClip (clip in hand)
        // rather than OnPreprocessAudio (path only).
        private void OnPostprocessAudioClip(AudioClip clip)
        {
            if (!assetImporter.importSettingsMissing) return;
            if (assetImporter is not AudioImporter importer) return;

            if (AudioImportOptimizer.ApplyTo(importer, assetPath, clip.length))
                LogHelper.Log("Audio", $"Applied default import settings to new clip {assetPath}.");
        }
    }
}
