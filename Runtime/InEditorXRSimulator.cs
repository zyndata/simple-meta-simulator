using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SMS
{
	/// <summary>
	/// Core in-editor XR simulator. Drives an OVRCameraRig with mouse/keyboard input when no real
	/// headset is present, by overwriting the rig anchors right after OVR updates them
	/// (via OVRCameraRig.UpdatedAnchors) and feeding controller state into the OVR input layer
	/// (via OVRInputStateInjector). Works with any OVR-based rig (Meta Interaction SDK, custom hand systems)
	/// without referencing those systems, because everything reads from the same OVR source.
	/// </summary>
	[DefaultExecutionOrder(-50)]
	public class InEditorXRSimulator : MonoBehaviour
	{
		private const float DEFAULT_EYE_HEIGHT = 1.6f;

		private SimulatorConfig config;
		private SimulatedRigState state;
		private OVRInputStateInjector inputInjector;
		private OVREventInvoker eventInvoker;
		private ISDKControllerInjector isdkInjector;
		private SimulatorInputActions actions;

		private object cameraRig;
		private Transform trackingSpace;
		private Transform centerEyeAnchor;
		private Transform leftHandAnchor;
		private Transform rightHandAnchor;
		private Transform leftControllerAnchor;
		private Transform rightControllerAnchor;

		private bool eventsRaised;
		private Vector2 headEuler;
		private ActiveMoveTarget cycleTarget = ActiveMoveTarget.Both;

		private Vector3 leftHandPos;
		private Vector3 rightHandPos;
		private Vector3 leftHandBaseOffset;
		private Vector3 rightHandBaseOffset;
		private float headYaw;

		private bool leftGrabHeld;
		private bool leftGripHeld;
		private bool rightGrabHeld;
		private bool rightGripHeld;

		private bool leftGrabWasDown;
		private bool leftGripWasDown;
		private bool rightGrabWasDown;
		private bool rightGripWasDown;

		private bool leftPrimaryHeld;
		private bool leftSecondaryHeld;
		private bool rightPrimaryHeld;
		private bool rightSecondaryHeld;

		private bool leftPrimaryWasDown;
		private bool leftSecondaryWasDown;
		private bool rightPrimaryWasDown;
		private bool rightSecondaryWasDown;

		public static InEditorXRSimulator Active { get; private set; }

		public string CurrentCycleTargetName => cycleTarget.ToString();

		public void Initialize (SimulatorConfig simulatorConfig, InputActionAsset controls)
		{
			Active = this;
			config = simulatorConfig;
			state = new SimulatedRigState();
			state.ResetToDefault(config.StartingEyeHeight);

			actions = new SimulatorInputActions(controls);
			actions.Enable();

			inputInjector = new OVRInputStateInjector();
			eventInvoker = new OVREventInvoker();
			isdkInjector = new ISDKControllerInjector();

			TryBindRig();
		}

		private void OnDestroy ()
		{
			Active = null;
			if (actions != null)
			{
				actions.Disable();
			}

			UnhookAnchors();

			if (eventInvoker != null)
			{
				eventInvoker.RaiseDisconnectedSequence(config);
			}
		}

		private void Update ()
		{
			if (IsRigBound() == false)
			{
				TryBindRig();
				return;
			}

			ReadInput(Time.deltaTime);

			if (inputInjector != null)
			{
				inputInjector.Inject(state);
			}
		}

		private bool IsRigBound ()
		{
			// cameraRig is typed as object, so Unity's fake-null equality does not apply.
			// Cast to UnityEngine.Object to detect a rig that was destroyed by a scene change.
			UnityEngine.Object rig = cameraRig as UnityEngine.Object;
			return rig != null;
		}

		private void TryBindRig ()
		{
			Type rigType = Type.GetType("OVRCameraRig, Oculus.VR");

			if (rigType == null)
			{
				return;
			}

			UnityEngine.Object found = FindFirstObjectByType(rigType, FindObjectsInactive.Include);

			if (found == null)
			{
				return;
			}

			cameraRig = found;
			eventsRaised = false;
			CacheAnchors(rigType, found);
			HookAnchors(rigType, found);
			state.ResetToDefault(config.StartingEyeHeight);
			headEuler = Vector2.zero;
			headYaw = 0f;
			leftHandPos = state.LeftHandLocalPosition;
			rightHandPos = state.RightHandLocalPosition;
			leftHandBaseOffset = state.LeftHandLocalPosition - state.HeadLocalPosition;
			rightHandBaseOffset = state.RightHandLocalPosition - state.HeadLocalPosition;

			if (isdkInjector != null)
			{
				isdkInjector.Bind(state, trackingSpace, leftControllerAnchor, rightControllerAnchor);
			}
		}

		private void CacheAnchors (Type rigType, UnityEngine.Object rig)
		{
			trackingSpace = GetAnchor(rigType, rig, "trackingSpace");
			centerEyeAnchor = GetAnchor(rigType, rig, "centerEyeAnchor");
			leftHandAnchor = GetAnchor(rigType, rig, "leftHandAnchor");
			rightHandAnchor = GetAnchor(rigType, rig, "rightHandAnchor");
			leftControllerAnchor = GetAnchor(rigType, rig, "leftControllerAnchor");
			rightControllerAnchor = GetAnchor(rigType, rig, "rightControllerAnchor");
		}

		private void HookAnchors (Type rigType, UnityEngine.Object rig)
		{
		}

		private void UnhookAnchors ()
		{
		}

		private void LateUpdate ()
		{
			if (IsRigBound() == false)
			{
				return;
			}

			if (eventsRaised == false && eventInvoker.HasSubscriber("HMDMounted") == true)
			{
				eventsRaised = true;
				eventInvoker.RaiseConnectedSequence(config);
			}

			ApplyPoses();
		}

		private void ReadInput (float deltaTime)
		{
			UpdateHeadLook(deltaTime);
			UpdateActiveTarget();
			UpdateMovement(deltaTime);
			UpdateHandInput();
		}

		private void UpdateHeadLook (float deltaTime)
		{
			if (actions.LookModifier() == false)
			{
				return;
			}

			Vector2 look = actions.LookDelta() * config.LookSensitivity;
			headEuler.x += look.x;
			headEuler.y += config.InvertLookY == true ? look.y : -look.y;
			headEuler.y = Mathf.Clamp(headEuler.y, -89f, 89f);
			state.HeadLocalRotation = Quaternion.Euler(headEuler.y, headEuler.x, 0f);
			headYaw = headEuler.x;
		}

		private void UpdateMovement (float deltaTime)
		{
			Vector2 move = actions.Move();
			float vertical = actions.Vertical();

			if (move == Vector2.zero && vertical == 0f)
			{
				return;
			}

			Quaternion yaw = Quaternion.Euler(0f, headYaw, 0f);
			Vector3 delta = yaw * new Vector3(move.x, 0f, move.y) * (config.MoveSpeed * deltaTime);
			delta.y += vertical * config.VerticalSpeed * deltaTime;

			ActiveMoveTarget target = ResolveMoveTarget();

			if (target == ActiveMoveTarget.Both)
			{
				state.HeadLocalPosition += delta;
				return;
			}

			if (target == ActiveMoveTarget.Head)
			{
				state.HeadLocalPosition += delta;
				return;
			}

			if (target == ActiveMoveTarget.Left)
			{
				leftHandPos += delta;
				return;
			}

			rightHandPos += delta;
		}

		private ActiveMoveTarget ResolveMoveTarget ()
		{
			if (config.HandActivationMode == HandActivationMode.CycleKey)
			{
				return cycleTarget;
			}

			return ActiveMoveTarget.Both;
		}

		private void UpdateActiveTarget ()
		{
			if (config.HandActivationMode != HandActivationMode.CycleKey)
			{
				return;
			}

			if (actions.CyclePressed() == false)
			{
				return;
			}

			cycleTarget = NextCycleTarget(cycleTarget);
		}

		private ActiveMoveTarget NextCycleTarget (ActiveMoveTarget current)
		{
			if (current == ActiveMoveTarget.Both)
			{
				return ActiveMoveTarget.Left;
			}

			if (current == ActiveMoveTarget.Left)
			{
				return ActiveMoveTarget.Right;
			}

			if (current == ActiveMoveTarget.Right)
			{
				return ActiveMoveTarget.Head;
			}

			return ActiveMoveTarget.Both;
		}

		private void UpdateHandPose ()
		{
			if (ResolveMoveTarget() == ActiveMoveTarget.Both)
			{
				Quaternion yaw = Quaternion.Euler(0f, headYaw, 0f);
				state.LeftHandLocalPosition = state.HeadLocalPosition + yaw * leftHandBaseOffset;
				state.RightHandLocalPosition = state.HeadLocalPosition + yaw * rightHandBaseOffset;
				state.LeftHandLocalRotation = yaw;
				state.RightHandLocalRotation = yaw;
				leftHandPos = state.LeftHandLocalPosition;
				rightHandPos = state.RightHandLocalPosition;
				return;
			}

			state.LeftHandLocalPosition = leftHandPos;
			state.RightHandLocalPosition = rightHandPos;
		}

		private void UpdateHandInput ()
		{
			bool leftGrabDown = actions.LeftGrab();
			bool leftGripDown = actions.LeftGrip();
			bool rightGrabDown = actions.RightGrab();
			bool rightGripDown = actions.RightGrip();

			if (leftGrabDown == true && leftGrabWasDown == false)
			{
				leftGrabHeld = leftGrabHeld == false;
			}

			if (leftGripDown == true && leftGripWasDown == false)
			{
				leftGripHeld = leftGripHeld == false;
			}

			if (rightGrabDown == true && rightGrabWasDown == false)
			{
				rightGrabHeld = rightGrabHeld == false;
			}

			if (rightGripDown == true && rightGripWasDown == false)
			{
				rightGripHeld = rightGripHeld == false;
			}

			leftGrabWasDown = leftGrabDown;
			leftGripWasDown = leftGripDown;
			rightGrabWasDown = rightGrabDown;
			rightGripWasDown = rightGripDown;

			HandInputState left = state.LeftInput;
			left.IndexTrigger = leftGrabHeld == true ? 1f : 0f;
			left.HandTrigger = leftGripHeld == true ? 1f : 0f;
			left.PrimaryButton = ResolveButton(actions.LeftPrimary(), ref leftPrimaryHeld, ref leftPrimaryWasDown);
			left.SecondaryButton = ResolveButton(actions.LeftSecondary(), ref leftSecondaryHeld, ref leftSecondaryWasDown);
			state.LeftInput = left;

			HandInputState right = state.RightInput;
			right.IndexTrigger = rightGrabHeld == true ? 1f : 0f;
			right.HandTrigger = rightGripHeld == true ? 1f : 0f;
			right.PrimaryButton = ResolveButton(actions.RightPrimary(), ref rightPrimaryHeld, ref rightPrimaryWasDown);
			right.SecondaryButton = ResolveButton(actions.RightSecondary(), ref rightSecondaryHeld, ref rightSecondaryWasDown);
			state.RightInput = right;
		}

		private bool ResolveButton (bool isDown, ref bool held, ref bool wasDown)
		{
			if (config.FaceButtonInputMode == ButtonInputMode.Toggle)
			{
				if (isDown == true && wasDown == false)
				{
					held = held == false;
				}

				wasDown = isDown;
				return held;
			}

			wasDown = isDown;
			held = isDown;
			return isDown;
		}

		private void ApplyPoses ()
		{
			if (centerEyeAnchor != null)
			{
				centerEyeAnchor.localPosition = state.HeadLocalPosition;
				centerEyeAnchor.localRotation = state.HeadLocalRotation;
			}

			UpdateHandPose();

			ApplyHand(leftHandAnchor, leftControllerAnchor, state.LeftHandLocalPosition, state.LeftHandLocalRotation);
			ApplyHand(rightHandAnchor, rightControllerAnchor, state.RightHandLocalPosition, state.RightHandLocalRotation);

			if (isdkInjector != null)
			{
				if (isdkInjector.IsActive() == false)
				{
					isdkInjector.Bind(state, trackingSpace, leftControllerAnchor, rightControllerAnchor);
				}

				isdkInjector.Apply();
			}
		}

		private void ApplyHand (Transform handAnchor, Transform controllerAnchor, Vector3 localPos, Quaternion localRot)
		{
			if (handAnchor != null)
			{
				handAnchor.localPosition = localPos;
				handAnchor.localRotation = localRot;
			}

			if (controllerAnchor != null && controllerAnchor.parent == handAnchor)
			{
				controllerAnchor.localPosition = Vector3.zero;
				controllerAnchor.localRotation = Quaternion.identity;
			}
		}

		private Transform GetAnchor (Type rigType, UnityEngine.Object rig, string propertyName)
		{
			var prop = rigType.GetProperty(propertyName);

			if (prop == null)
			{
				return null;
			}

			return prop.GetValue(rig) as Transform;
		}

		private enum ActiveMoveTarget
		{
			Head = 0,
			Left = 1,
			Right = 2,
			Both = 3
		}
	}
}
