using UnityEngine;

namespace QuantumUser.View.Util
{
    // Draws a "+" button next to a ScriptableObject reference field. Clicking it creates a new
    // instance of the field's type, saves it as a project asset, and assigns it to the field -
    // skips the usual "right-click > Create > ... > drag into slot" dance. Defaults to
    // Assets/QuantumUser/Resources/<TypeName>/, pass folder to save elsewhere (e.g. next to a
    // view-only asset type that isn't loaded via Resources).
    public class CreateAssetButtonAttribute : PropertyAttribute
    {
        public readonly string Folder;

        public CreateAssetButtonAttribute(string folder = null)
        {
            Folder = folder;
        }
    }
}
