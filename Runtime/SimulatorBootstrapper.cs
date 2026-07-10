using System;
using System.Reflection;
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

		private static bool pluginResolved;
		private static PropertyInfo hmdPresentProperty;
		private static PropertyInfo userPresentProperty;

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
				Debug.Log("[SMS] Real headset detected (Quest Link active or headset worn) - simulator will not start.");
				return;
			}

			GameObject host = new GameObject(SimulatorConstants.SIMULATOR_OBJECT_NAME);
			UnityEngine.Object.DontDestroyOnLoad(host);
			activeInstance = host.AddComponent<InEditorXRSimulator>();
			activeInstance.Initialize(config, SimulatorConfigProvider.Controls);
		}

		/// <summary>
		/// True when a real headset is reachable: hmdPresent covers a headset connected through
		/// Quest Link even while it sits on the desk; userPresent only turns true while it is
		/// actually worn. Public so the editor layer (toolbar icon) can show the blocked state.
		/// Reflection is resolved once; safe to poll.
		/// </summary>
		public static bool IsRealHeadsetPresent ()
		{
			if (pluginResolved == false)
			{
				pluginResolved = true;
				Type plugin = Type.GetType("OVRPlugin, Oculus.VR");

				if (plugin != null)
				{
					hmdPresentProperty = plugin.GetProperty("hmdPresent");
					userPresentProperty = plugin.GetProperty("userPresent");
				}
			}

			return ReadBoolProperty(hmdPresentProperty) == true || ReadBoolProperty(userPresentProperty) == true;
		}

		private static bool ReadBoolProperty (PropertyInfo property)
		{
			if (property == null)
			{
				return false;
			}

			try
			{
				object value = property.GetValue(null);
				return value is bool present && present == true;
			}
			catch
			{
				return false;
			}
		}
	}
}
