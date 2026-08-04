using System.ComponentModel;

// Extends SRDebugger's SROptions.Current panel with a toggle for LogHelper.Disabled, so
// LogHelper.Log/Warn/Error can be silenced at runtime while profiling in the Editor or a
// Development Build - where [Conditional("UNITY_EDITOR")/("DEVELOPMENT_BUILD")] doesn't strip
// anything. See Assets/_QuantumUser/View/Util/Shared/LogHelper.cs.
//
// Must live outside any asmdef/asmref'd folder (i.e. in the default Assembly-CSharp) - SROptions
// itself (Assets/StompyRobot/SROptions/SROptions.cs) has no asmdef, so it compiles into
// Assembly-CSharp too, and C# only merges a partial class within a single assembly. A copy of
// this file placed under Assets/_QuantumUser/View/Util/Shared/ (which has a Quantum.Unity.asmref)
// would silently become a disconnected type SRDebugger never sees, instead of a compile error.
public partial class SROptions
{
    [Category("Logging")]
    public bool DisableViewLogging
    {
        get => QuantumUser.View.Util.LogHelper.Disabled;
        set => QuantumUser.View.Util.LogHelper.Disabled = value;
    }
}
