namespace Quantum
{
#if QUANTUM_UNITY
	public sealed class KCCHeaderAttribute  : UnityEngine.HeaderAttribute  { public KCCHeaderAttribute(string header)   : base(header)  {} }
	public sealed class KCCTooltipAttribute : UnityEngine.TooltipAttribute { public KCCTooltipAttribute(string tooltip) : base(tooltip) {} }
#else
	public sealed class KCCHeaderAttribute  : System.Attribute { public KCCHeaderAttribute(string header)   {} }
	public sealed class KCCTooltipAttribute : System.Attribute { public KCCTooltipAttribute(string tooltip) {} }
#endif
}
