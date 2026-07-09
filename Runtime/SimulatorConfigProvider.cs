using UnityEngine.InputSystem;

namespace SMS
{
	/// <summary>
	/// Runtime access point for the simulator configuration. The editor layer pushes a config
	/// instance and the optional controls asset here before play mode starts (no Resources asset,
	/// so nothing is packaged into a build). When no config has been pushed, the simulator stays disabled.
	/// </summary>
	public static class SimulatorConfigProvider
	{
		public static SimulatorConfig Current { get; set; }
		public static InputActionAsset Controls { get; set; }
	}
}
