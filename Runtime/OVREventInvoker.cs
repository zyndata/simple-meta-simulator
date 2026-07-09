using System;
using System.Reflection;
using UnityEngine;

namespace SMS
{
	/// <summary>
	/// Raises OVRManager lifecycle events (HMD mounted/unmounted, input focus, tracking, HMD
	/// acquired/lost) so systems that wait for them behave as if a real headset connected. The
	/// events are static Action fields on OVRManager; we fetch and invoke their backing delegates
	/// via reflection. Which events fire is gated by the config toggles (all on by default).
	/// </summary>
	public class OVREventInvoker
	{
		private bool resolved;
		private Type managerType;

		public void RaiseConnectedSequence (SimulatorConfig config)
		{
			EnsureResolved();

			if (config.RaiseHmdAcquiredEvents == true)
			{
				Raise("HMDAcquired");
			}

			if (config.RaiseHmdMountedEvents == true)
			{
				Raise("HMDMounted");
			}

			if (config.RaiseInputFocusEvents == true)
			{
				Raise("InputFocusAcquired");
			}

			if (config.RaiseTrackingEvents == true)
			{
				Raise("TrackingAcquired");
			}
		}

		public void RaiseDisconnectedSequence (SimulatorConfig config)
		{
			EnsureResolved();

			if (config.RaiseInputFocusEvents == true)
			{
				Raise("InputFocusLost");
			}

			if (config.RaiseHmdMountedEvents == true)
			{
				Raise("HMDUnmounted");
			}

			if (config.RaiseTrackingEvents == true)
			{
				Raise("TrackingLost");
			}

			if (config.RaiseHmdAcquiredEvents == true)
			{
				Raise("HMDLost");
			}
		}

		public bool HasSubscriber (string eventName)
		{
			EnsureResolved();

			if (managerType == null)
			{
				return false;
			}

			BindingFlags flags = (BindingFlags)(0x8 | 0x10 | 0x20);
			FieldInfo field = managerType.GetField(eventName, flags);

			if (field == null)
			{
				return false;
			}

			Delegate action = field.GetValue(null) as Delegate;
			return action != null;
		}

		public void RaiseSingle (string eventName)
		{
			EnsureResolved();
			Raise(eventName);
		}

		private void Raise (string eventName)
		{
			if (managerType == null)
			{
				return;
			}

			BindingFlags flags = (BindingFlags)(0x8 | 0x10 | 0x20);
			FieldInfo field = managerType.GetField(eventName, flags);

			if (field == null)
			{
				return;
			}

			Action action = field.GetValue(null) as Action;

			if (action == null)
			{
				return;
			}

			try
			{
				action.Invoke();
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[SMS] OVR event '" + eventName + "' threw: " + exception.Message);
			}
		}

		private void EnsureResolved ()
		{
			if (resolved == true)
			{
				return;
			}

			resolved = true;
			managerType = Type.GetType("OVRManager, Oculus.VR");
		}
	}
}
