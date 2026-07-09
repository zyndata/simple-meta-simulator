using System;
using UnityEngine;

namespace SMS
{
	/// <summary>
	/// Entry point that spawns the simulator automatically when play mode starts, if the config is
	/// enabled and no real headset is present. Editor-only: guarded by Application.isEditor so it
	/// never runs in a player build (the whole assembly is Editor-platform anyway).
	/// </summary>
	public static class SimulatorBootstrapper
	{
		private static InEditorXRSimulator activeInstance;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Bootstrap ()
		{
			if (Application.isEditor == false)
			{
				return;
			}

			SimulatorConfig config = SimulatorConfigProvider.Current;

			if (config == null || config.Enabled == false)
			{
				return;
			}

			if (IsRealHeadsetPresent() == true)
			{
				return;
			}

			GameObject host = new GameObject(SimulatorConstants.SIMULATOR_OBJECT_NAME);
			UnityEngine.Object.DontDestroyOnLoad(host);
			activeInstance = host.AddComponent<InEditorXRSimulator>();
			activeInstance.Initialize(config, SimulatorConfigProvider.Controls);
		}

		private static bool IsRealHeadsetPresent ()
		{
			Type plugin = Type.GetType("OVRPlugin, Oculus.VR");

			if (plugin == null)
			{
				return false;
			}

			var prop = plugin.GetProperty("userPresent");

			if (prop == null)
			{
				return false;
			}

			try
			{
				object value = prop.GetValue(null);
				return value is bool present && present == true;
			}
			catch
			{
				return false;
			}
		}
	}
}
