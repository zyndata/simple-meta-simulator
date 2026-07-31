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
		/// True when a real headset is driving the current session: hmdPresent covers a headset connected
		/// through Quest Link even while it sits on the desk; userPresent only turns true while it is actually
		/// worn. Both come from the native runtime, so they only answer once it is up - in play mode. Outside
		/// play mode they read false even with Link running, which is why the toolbar light cannot warn about
		/// Link before you press Play; see the note below. Public so the editor layer (toolbar icon) can show
		/// the blocked state. Reflection is resolved once; safe to poll.
		///
		/// Do NOT widen this with OVRPlugin.GetSystemHeadsetType(). It is the only OVRPlugin signal that
		/// answers in edit mode, but it reports the configured / last known headset rather than a live
		/// connection: measured returning Meta_Link_Quest_3 in edit mode with Link fully disconnected, which
		/// pinned the toolbar light orange. Every other bool on OVRPlugin (positionTracked, hasVrFocus,
		/// hasInputFocus, headphonesPresent, ...) is already false with the runtime down, so it carries no
		/// connection information either.
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
