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

		private object lTouchValue;
		private object rTouchValue;
		private int lTouchInt;
		private int rTouchInt;
		private Type vector2fType;
		private FieldInfo vec2xField;
		private FieldInfo vec2yField;

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

			if (activeControllerTypeField != null && touchControllerValue != null)
			{
				activeControllerTypeField.SetValue(null, touchControllerValue);
			}

			if (connectedControllerTypesField != null && touchControllerValue != null)
			{
				connectedControllerTypesField.SetValue(null, touchControllerValue);
			}

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
					WriteState(controller, state.LeftInput, true);
				}
				else if (typeInt == rTouchInt)
				{
					WriteState(controller, state.RightInput, false);
				}
			}
		}

		private void WriteState (object controller, HandInputState input, bool isLeft)
		{
			if (previousStateField != null)
			{
				previousStateField.SetValue(controller, currentStateField.GetValue(controller));
			}

			object boxedState = currentStateField.GetValue(controller);
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

				lIndexTriggerField.SetValue(boxedState, input.IndexTrigger);
				lHandTriggerField.SetValue(boxedState, input.HandTrigger);
				WriteThumbstick(boxedState, lThumbstickField, input.Thumbstick);
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

				rIndexTriggerField.SetValue(boxedState, input.IndexTrigger);
				rHandTriggerField.SetValue(boxedState, input.HandTrigger);
				WriteThumbstick(boxedState, rThumbstickField, input.Thumbstick);
			}

			buttonsField.SetValue(boxedState, buttons);

			if (connectedField != null)
			{
				connectedField.SetValue(boxedState, (uint)CONNECTED_TOUCH_MASK);
			}

			currentStateField.SetValue(controller, boxedState);
		}

		private void WriteThumbstick (object boxedState, FieldInfo thumbstickField, Vector2 value)
		{
			if (thumbstickField == null || vector2fType == null)
			{
				return;
			}

			object boxedVec = thumbstickField.GetValue(boxedState);

			if (boxedVec == null)
			{
				boxedVec = Activator.CreateInstance(vector2fType);
			}

			if (vec2xField != null)
			{
				vec2xField.SetValue(boxedVec, value.x);
			}

			if (vec2yField != null)
			{
				vec2yField.SetValue(boxedVec, value.y);
			}

			thumbstickField.SetValue(boxedState, boxedVec);
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

			lTouchValue = Enum.Parse(controllerEnum, "LTouch");
			rTouchValue = Enum.Parse(controllerEnum, "RTouch");
			lTouchInt = Convert.ToInt32(lTouchValue);
			rTouchInt = Convert.ToInt32(rTouchValue);
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
	}
}
