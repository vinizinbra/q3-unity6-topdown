namespace Quantum.Editor
{
    using Quantum.CodeGen;

    public static partial class QuantumCodeGenSettings
    {
        static partial void GetCodeGenFolderPathUser(ref string path) { path = "Assets/_QuantumUser/Simulation/Generated"; }
        static partial void GetCodeGenUnityRuntimeFolderPathUser(ref string path) { path = "Assets/_QuantumUser/View/Generated"; }
        static partial void GetOptionsUser(ref GeneratorOptions options) { }
    }
}