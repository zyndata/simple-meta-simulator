using UnityEditor;
using UnityEngine;

namespace SMS.Editor
{
	/// <summary>
	/// Three-state status shown by the toolbar button, shared by both toolbar implementations
	/// (the Unity 6.3+ main toolbar element and the legacy visual tree injection) so the light
	/// and its tooltip stay identical across editor versions.
	/// </summary>
	internal enum ToolbarState
	{
		Disabled = 0,
		Enabled = 1,
		Blocked = 2
	}

	internal static class SimulatorToolbarStatus
	{
		public const double REFRESH_INTERVAL = 0.5d;

		private const string ACTIVE_ICON = "greenLight";
		private const string BLOCKED_ICON = "orangeLight";
		private const string INACTIVE_ICON = "lightOff";

		private const string ACTIVE_TOOLTIP = "Simple Meta Simulator: ENABLED";
		private const string BLOCKED_TOOLTIP = "Simple Meta Simulator: ENABLED, but a real headset (Quest Link) is detected - the simulator will not start.";
		private const string INACTIVE_TOOLTIP = "Simple Meta Simulator: DISABLED";

		public static ToolbarState Resolve ()
		{
			if (SimulatorSettings.IsEnabled() == false)
			{
				return ToolbarState.Disabled;
			}

			if (SimulatorBootstrapper.IsRealHeadsetPresent() == true)
			{
				return ToolbarState.Blocked;
			}

			return ToolbarState.Enabled;
		}

		public static Texture2D GetIcon (ToolbarState state)
		{
			if (state == ToolbarState.Blocked)
			{
				return EditorGUIUtility.IconContent(BLOCKED_ICON).image as Texture2D;
			}

			if (state == ToolbarState.Enabled)
			{
				return EditorGUIUtility.IconContent(ACTIVE_ICON).image as Texture2D;
			}

			return EditorGUIUtility.IconContent(INACTIVE_ICON).image as Texture2D;
		}

		public static string GetTooltip (ToolbarState state)
		{
			if (state == ToolbarState.Blocked)
			{
				return BLOCKED_TOOLTIP;
			}

			if (state == ToolbarState.Enabled)
			{
				return ACTIVE_TOOLTIP;
			}

			return INACTIVE_TOOLTIP;
		}
	}
}
