using System;
using UnityEngine;

namespace SMS
{
	/// <summary>
	/// Serializable configuration for the in-editor XR simulator. Persisted by the editor layer
	/// and consumed by <see cref="InEditorXRSimulator"/> at runtime. All fields have safe defaults
	/// so the simulator works out of the box without any manual wiring.
	/// </summary>
	[Serializable]
	public class SimulatorConfig
	{
		[Header(SimulatorConstants.HEADER_GENERAL)]
		[SerializeField]
		private bool enabled = false;

		[Header(SimulatorConstants.HEADER_MOVEMENT)]
		[SerializeField]
		private float moveSpeed = 1.5f;

		[SerializeField]
		private float verticalSpeed = 1.0f;

		[SerializeField]
		private float startingEyeHeight = 1.6f;

		[Header(SimulatorConstants.HEADER_LOOK)]
		[SerializeField]
		private float lookSensitivity = 0.15f;

		[SerializeField]
		private bool invertLookY = false;

		[Header(SimulatorConstants.HEADER_HANDS)]
		[SerializeField]
		private float handMoveSpeed = 0.6f;

		[SerializeField]
		private float handDepthSpeed = 0.4f;

		[SerializeField]
		private HandActivationMode handActivationMode = HandActivationMode.CycleKey;

		[SerializeField]
		private ButtonInputMode faceButtonInputMode = ButtonInputMode.Held;

		[Header(SimulatorConstants.HEADER_EVENTS)]
		[SerializeField]
		private bool raiseHmdMountedEvents = true;

		[SerializeField]
		private bool raiseInputFocusEvents = true;

		[SerializeField]
		private bool raiseTrackingEvents = true;

		[SerializeField]
		private bool raiseHmdAcquiredEvents = true;

		public bool Enabled { get => enabled; set => enabled = value; }
		public float MoveSpeed => moveSpeed;
		public float VerticalSpeed => verticalSpeed;
		public float StartingEyeHeight => startingEyeHeight;
		public float LookSensitivity => lookSensitivity;
		public bool InvertLookY => invertLookY;
		public float HandMoveSpeed => handMoveSpeed;
		public float HandDepthSpeed => handDepthSpeed;
		public HandActivationMode HandActivationMode => handActivationMode;
		public ButtonInputMode FaceButtonInputMode => faceButtonInputMode;
		public bool RaiseHmdMountedEvents => raiseHmdMountedEvents;
		public bool RaiseInputFocusEvents => raiseInputFocusEvents;
		public bool RaiseTrackingEvents => raiseTrackingEvents;
		public bool RaiseHmdAcquiredEvents => raiseHmdAcquiredEvents;
	}
}
