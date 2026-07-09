using UnityEngine;

namespace SMS
{
	/// <summary>
	/// Holds the current simulated state that is written into the OVR rig each frame:
	/// head/hand local poses (relative to the tracking space) and per-hand button/axis input.
	/// Pure data - no Unity lifecycle. Consumed by <see cref="InEditorXRSimulator"/> and
	/// <see cref="OVRInputStateInjector"/>.
	/// </summary>
	public class SimulatedRigState
	{
		public Vector3 HeadLocalPosition;
		public Quaternion HeadLocalRotation = Quaternion.identity;

		public Vector3 LeftHandLocalPosition;
		public Quaternion LeftHandLocalRotation = Quaternion.identity;

		public Vector3 RightHandLocalPosition;
		public Quaternion RightHandLocalRotation = Quaternion.identity;

		public HandInputState LeftInput = HandInputState.Default;
		public HandInputState RightInput = HandInputState.Default;

		public void ResetToDefault (float eyeHeight)
		{
			HeadLocalPosition = new Vector3(0f, eyeHeight, 0f);
			HeadLocalRotation = Quaternion.identity;

			LeftHandLocalPosition = new Vector3(-0.2f, eyeHeight - 0.45f, 0.4f);
			LeftHandLocalRotation = Quaternion.identity;

			RightHandLocalPosition = new Vector3(0.2f, eyeHeight - 0.45f, 0.4f);
			RightHandLocalRotation = Quaternion.identity;

			LeftInput = HandInputState.Default;
			RightInput = HandInputState.Default;
		}
	}

	/// <summary>
	/// Per-hand button and axis values, normalized 0..1 for triggers/grips.
	/// </summary>
	public struct HandInputState
	{
		public float IndexTrigger;
		public float HandTrigger;
		public bool PrimaryButton;
		public bool SecondaryButton;
		public bool ThumbstickButton;
		public Vector2 Thumbstick;

		public static HandInputState Default => new HandInputState
		{
			IndexTrigger = 0f,
			HandTrigger = 0f,
			PrimaryButton = false,
			SecondaryButton = false,
			ThumbstickButton = false,
			Thumbstick = Vector2.zero
		};
	}
}
