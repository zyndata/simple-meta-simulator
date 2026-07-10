using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace SMS
{
	/// <summary>
	/// Feeds simulated controller input into the OVR input layer by overwriting the cached
	/// ControllerState struct on each OVRInput.OVRControllerBase (LTouch / RTouch) after OVRInput
	/// has updated. Because the Meta Interaction SDK and everything else read controller
	/// state through OVRInput.Get(...), writing here makes them all respond to the simulator without
	/// any direct reference to those systems. All access is reflection-based and cached once.
	/// Writes are skipped once previousState == currentState == the simulated input, so steady-state
	/// frames (no key changes) allocate nothing; the skip keeps GetDown/GetUp edges intact because
	/// a change is always written twice (once to move currentState, once to move previousState).
	/// </summary>
	public class OVRInputStateInjector
	{
		private const uint L_INDEX_TRIGGER = 0x10000000;
		private const uint L_HAND_TRIGGER = 0x20000000;
		private const uint R_INDEX_TRIGGER = 0x4000000;
		private const uint R_HAND_TRIGGER = 0x8000000;
		private const uint BTN_A = 0x1;
		private const uint BTN_B = 0x2;
		private const uint BTN_X = 0x100;
		private const uint BTN_Y = 0x200;

		private const float TRIGGER_PRESS_THRESHOLD = 0.5f;
		private const byte CONNECTED_TOUCH_MASK = 0x10;

		private bool resolved;
		private bool resolveFailed;

		private IList controllersList;
		private FieldInfo controllerTypeField;
		private FieldInfo currentStateField;
		private FieldInfo previousStateField;
		private FieldInfo activeControllerTypeField;
		private FieldInfo connectedControllerTypesField;
		private object touchControllerValue;

		private FieldInfo buttonsField;
		private FieldInfo connectedField;
		private FieldInfo lIndexTriggerField;
		private FieldInfo rIndexTriggerField;
		private FieldInfo lHandTriggerField;
		private FieldInfo rHandTriggerField;
		private FieldInfo lThumbstickField;
		private FieldInfo rThumbstickField;

		private int lTouchInt;
		private int rTouchInt;
		private Type vector2fType;
		private FieldInfo vec2xField;
		private FieldInfo vec2yField;

		private readonly HandChannel leftChannel = new HandChannel();
		private readonly HandChannel rightChannel = new HandChannel();

		public void Inject (SimulatedRigState state)
		{
			if (resolveFailed == true)
			{
				return;
			}

			if (resolved == false)
			{
				Resolve();
			}

			if (resolveFailed == true || controllersList == null)
			{
				return;
			}

			if (leftChannel.Controller == null || rightChannel.Controller == null)
			{
				ResolveControllers();
			}

			if (activeControllerTypeField != null && touchControllerValue != null)
			{
				activeControllerTypeField.SetValue(null, touchControllerValue);
			}

			if (connectedControllerTypesField != null && touchControllerValue != null)
			{
				connectedControllerTypesField.SetValue(null, touchControllerValue);
			}

			WriteState(leftChannel, state.LeftInput, true);
			WriteState(rightChannel, state.RightInput, false);
		}

		private void ResolveControllers ()
		{
			for (int i = 0; i < controllersList.Count; i++)
			{
				object controller = controllersList[i];

				if (controller == null)
				{
					continue;
				}

				int typeInt = Convert.ToInt32(controllerTypeField.GetValue(controller));

				if (typeInt == lTouchInt)
				{
					leftChannel.Controller = controller;
				}
				else if (typeInt == rTouchInt)
				{
					rightChannel.Controller = controller;
				}
			}
		}

		private void WriteState (HandChannel channel, HandInputState input, bool isLeft)
		{
			if (channel.Controller == null)
			{
				return;
			}

			// Steady state: previousState == currentState == input, nothing to move. Without the
			// native runtime OVRInput.Update does not touch these fields, so the skip is safe.
			if (channel.HasWritten == true && channel.LastInputStable == true && InputEquals(input, channel.LastInput) == true)
			{
				return;
			}

			if (channel.StateBox == null)
			{
				// Seed the reusable box from the controller's real state once; afterwards the box
				// always mirrors what currentState holds, so no per-frame GetValue boxing is needed.
				channel.StateBox = currentStateField.GetValue(channel.Controller);
			}

			if (previousStateField != null)
			{
				previousStateField.SetValue(channel.Controller, channel.StateBox);
			}

			uint buttons = 0;

			if (isLeft == true)
			{
				if (input.IndexTrigger > TRIGGER_PRESS_THRESHOLD)
				{
					buttons |= L_INDEX_TRIGGER;
				}

				if (input.HandTrigger > TRIGGER_PRESS_THRESHOLD)
				{
					buttons |= L_HAND_TRIGGER;
				}

				if (input.PrimaryButton == true)
				{
					buttons |= BTN_X;
				}

				if (input.SecondaryButton == true)
				{
					buttons |= BTN_Y;
				}

				lIndexTriggerField.SetValue(channel.StateBox, input.IndexTrigger);
				lHandTriggerField.SetValue(channel.StateBox, input.HandTrigger);
				WriteThumbstick(channel, lThumbstickField, input.Thumbstick);
			}
			else
			{
				if (input.IndexTrigger > TRIGGER_PRESS_THRESHOLD)
				{
					buttons |= R_INDEX_TRIGGER;
				}

				if (input.HandTrigger > TRIGGER_PRESS_THRESHOLD)
				{
					buttons |= R_HAND_TRIGGER;
				}

				if (input.PrimaryButton == true)
				{
					buttons |= BTN_A;
				}

				if (input.SecondaryButton == true)
				{
					buttons |= BTN_B;
				}

				rIndexTriggerField.SetValue(channel.StateBox, input.IndexTrigger);
				rHandTriggerField.SetValue(channel.StateBox, input.HandTrigger);
				WriteThumbstick(channel, rThumbstickField, input.Thumbstick);
			}

			buttonsField.SetValue(channel.StateBox, buttons);

			if (connectedField != null)
			{
				connectedField.SetValue(channel.StateBox, (uint)CONNECTED_TOUCH_MASK);
			}

			currentStateField.SetValue(channel.Controller, channel.StateBox);

			channel.LastInputStable = channel.HasWritten == true && InputEquals(input, channel.LastInput) == true;
			channel.LastInput = input;
			channel.HasWritten = true;
		}

		private void WriteThumbstick (HandChannel channel, FieldInfo thumbstickField, Vector2 value)
		{
			if (thumbstickField == null || vector2fType == null)
			{
				return;
			}

			if (channel.ThumbBox == null)
			{
				channel.ThumbBox = Activator.CreateInstance(vector2fType);
			}

			if (vec2xField != null)
			{
				vec2xField.SetValue(channel.ThumbBox, value.x);
			}

			if (vec2yField != null)
			{
				vec2yField.SetValue(channel.ThumbBox, value.y);
			}

			thumbstickField.SetValue(channel.StateBox, channel.ThumbBox);
		}

		private static bool InputEquals (HandInputState a, HandInputState b)
		{
			return a.IndexTrigger == b.IndexTrigger
				&& a.HandTrigger == b.HandTrigger
				&& a.PrimaryButton == b.PrimaryButton
				&& a.SecondaryButton == b.SecondaryButton
				&& a.ThumbstickButton == b.ThumbstickButton
				&& a.Thumbstick == b.Thumbstick;
		}

		private void Resolve ()
		{
			resolved = true;

			Type ovrInput = Type.GetType("OVRInput, Oculus.VR");

			if (ovrInput == null)
			{
				resolveFailed = true;
				return;
			}

			BindingFlags all = (BindingFlags)(0x4 | 0x8 | 0x10 | 0x20);

			FieldInfo controllersField = ovrInput.GetField("controllers", all);
			Type controllerEnum = ovrInput.GetNestedType("Controller", all);
			Type baseType = ovrInput.GetNestedType("OVRControllerBase", all);

			if (controllersField == null || controllerEnum == null || baseType == null)
			{
				resolveFailed = true;
				return;
			}

			controllersList = controllersField.GetValue(null) as IList;
			controllerTypeField = baseType.GetField("controllerType", all);
			currentStateField = baseType.GetField("currentState", all);
			previousStateField = baseType.GetField("previousState", all);

			if (controllersList == null || controllerTypeField == null || currentStateField == null)
			{
				resolveFailed = true;
				return;
			}

			lTouchInt = Convert.ToInt32(Enum.Parse(controllerEnum, "LTouch"));
			rTouchInt = Convert.ToInt32(Enum.Parse(controllerEnum, "RTouch"));
			touchControllerValue = Enum.Parse(controllerEnum, "Touch");
			activeControllerTypeField = ovrInput.GetField("activeControllerType", all);
			connectedControllerTypesField = ovrInput.GetField("connectedControllerTypes", all);

			Type stateType = currentStateField.FieldType;
			buttonsField = stateType.GetField("Buttons", all);
			connectedField = stateType.GetField("ConnectedControllers", all);
			lIndexTriggerField = stateType.GetField("LIndexTrigger", all);
			rIndexTriggerField = stateType.GetField("RIndexTrigger", all);
			lHandTriggerField = stateType.GetField("LHandTrigger", all);
			rHandTriggerField = stateType.GetField("RHandTrigger", all);
			lThumbstickField = stateType.GetField("LThumbstick", all);
			rThumbstickField = stateType.GetField("RThumbstick", all);

			if (lThumbstickField != null)
			{
				vector2fType = lThumbstickField.FieldType;
				vec2xField = vector2fType.GetField("x", all);
				vec2yField = vector2fType.GetField("y", all);
			}

			if (buttonsField == null || lIndexTriggerField == null || rIndexTriggerField == null)
			{
				resolveFailed = true;
			}
		}

		/// <summary>
		/// Per-hand injection state: the resolved OVRControllerBase, the reusable boxed
		/// ControllerState / thumbstick vector, and the last written input for the steady-state skip.
		/// </summary>
		private class HandChannel
		{
			public object Controller;
			public object StateBox;
			public object ThumbBox;
			public HandInputState LastInput;
			public bool LastInputStable;
			public bool HasWritten;
		}
	}
}
