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
		private HandActivationMode handActivationMode = HandActivationMode.CycleKey;

		[SerializeField]
		private float handRotateSensitivity = 0.15f;

		[SerializeField]
		private bool invertHandRotateY = false;

		[SerializeField]
		private ButtonInputMode faceButtonInputMode = ButtonInputMode.Held;

		[SerializeField]
		private ButtonInputMode grabGripInputMode = ButtonInputMode.Toggle;

		[SerializeField]
		private HandSimulationMode handSimulation = HandSimulationMode.Off;

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
		public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
		public float VerticalSpeed { get => verticalSpeed; set => verticalSpeed = value; }
		public float StartingEyeHeight { get => startingEyeHeight; set => startingEyeHeight = value; }
		public float LookSensitivity { get => lookSensitivity; set => lookSensitivity = value; }
		public bool InvertLookY { get => invertLookY; set => invertLookY = value; }
		public HandActivationMode HandActivationMode { get => handActivationMode; set => handActivationMode = value; }
		public float HandRotateSensitivity { get => handRotateSensitivity; set => handRotateSensitivity = value; }
		public bool InvertHandRotateY { get => invertHandRotateY; set => invertHandRotateY = value; }
		public ButtonInputMode FaceButtonInputMode { get => faceButtonInputMode; set => faceButtonInputMode = value; }
		public ButtonInputMode GrabGripInputMode { get => grabGripInputMode; set => grabGripInputMode = value; }
		public HandSimulationMode HandSimulation { get => handSimulation; set => handSimulation = value; }
		public bool RaiseHmdMountedEvents { get => raiseHmdMountedEvents; set => raiseHmdMountedEvents = value; }
		public bool RaiseInputFocusEvents { get => raiseInputFocusEvents; set => raiseInputFocusEvents = value; }
		public bool RaiseTrackingEvents { get => raiseTrackingEvents; set => raiseTrackingEvents = value; }
		public bool RaiseHmdAcquiredEvents { get => raiseHmdAcquiredEvents; set => raiseHmdAcquiredEvents = value; }
	}
}
