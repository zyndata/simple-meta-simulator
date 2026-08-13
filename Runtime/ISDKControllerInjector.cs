using System;
using System.Reflection;
using UnityEngine;

namespace SMS
{
	/// <summary>
	/// Feeds simulated controller poses into the Meta Interaction SDK (ISDK) controller data
	/// pipeline. ISDK reads controller poses from a native source (OVRInput.GetLocalControllerPosition)
	/// which returns zero without a headset, so the controller visuals sit at the origin. Each frame it
	/// overwrites every FromOVRControllerDataSource's ControllerDataAsset (RootPose, PointerPose,
	/// IsTracked, IsConnected, Input) so pollers of IController see simulated data, and it drives the
	/// ISDK transforms that ISDK itself would only move from native data (ControllerOffset grab points,
	/// ControllerPointerPose ray/poke/distance-grab origins) directly.
	/// All access is reflection-based; no reference to Oculus.Interaction is needed.
	/// </summary>
	public class ISDKControllerInjector
	{
		private const int SYNTHETIC_POSE_ORIGIN = 3;

		private const int MODEL_SELECT_MAX_ATTEMPTS = 300;

		private bool resolved;
		private bool resolveFailed;
		private bool modelsSelected;
		private int modelSelectAttempts;

		private Animator leftAnimator;
		private Animator rightAnimator;
		private int animGripHash;
		private int animTriggerHash;
		private int animButton1Hash;
		private int animButton2Hash;
		private bool animHashesReady;

		private object leftSource;
		private object rightSource;
		private MethodInfo getDataMethod;
		private FieldInfo rootPoseField;
		private FieldInfo rootPoseOriginField;
		private FieldInfo pointerPoseField;
		private FieldInfo pointerPoseOriginField;
		private FieldInfo isTrackedField;
		private FieldInfo isConnectedField;
		private FieldInfo isDataValidField;
		private FieldInfo inputField;
		private FieldInfo buttonUsageMaskField;
		private FieldInfo triggerField;
		private FieldInfo gripField;
		private Type buttonUsageType;
		private object[] boxedUsageMasks;
		private FieldInfo poseFieldPosition;
		private FieldInfo poseFieldRotation;
		private object boxedPoseOrigin;

		private SimulatedRigState state;
		private Transform isdkTrackingTransform;
		private Transform leftAnchor;
		private Transform rightAnchor;
		private Transform trackingSpace;

		private bool offsetsResolved;
		private Component[] leftOffsets;
		private Component[] rightOffsets;
		private FieldInfo offsetOffsetField;
		private FieldInfo offsetRotationField;

		private bool pointerPosesResolved;
		private Component[] leftPointerPoses;
		private Component[] rightPointerPoses;
		private FieldInfo pointerPoseOffsetField;
		private FieldInfo pointerPoseActiveField;

		private static readonly object BOXED_TRUE = true;
		private static readonly object BOXED_FALSE = false;

		private bool reportConnected = true;

		public void Bind (SimulatedRigState rigState, Transform trackingSpaceTransform, Transform leftControllerAnchor, Transform rightControllerAnchor)
		{
			// A change of tracking space means the scene changed and the previously resolved
			// interaction sources belong to a destroyed rig. Force a full re-resolve.
			if (trackingSpace != trackingSpaceTransform)
			{
				resolved = false;
				resolveFailed = false;
				modelsSelected = false;
				modelSelectAttempts = 0;
				offsetsResolved = false;
				pointerPosesResolved = false;
				leftPointerPoses = null;
				rightPointerPoses = null;
				leftSource = null;
				rightSource = null;
				poseFieldPosition = null;
				poseFieldRotation = null;
				boxedPoseOrigin = null;
			}

			state = rigState;
			trackingSpace = trackingSpaceTransform;
			leftAnchor = leftControllerAnchor;
			rightAnchor = rightControllerAnchor;

			if (resolved == false && resolveFailed == false)
			{
				Resolve();
			}
		}

		public bool IsActive ()
		{
			return resolveFailed == false && (leftSource != null || rightSource != null);
		}

		// Whether the injected controllers report themselves as connected. ControllerRef.Active is
		// Controller.IsConnected, which is IsDataValid && IsConnected on the injected asset, so
		// clearing this is what makes a rig with both interactor sets switch to its hand-only
		// branch (see HandSimulationMode.HandsOnly).
		public void SetReportConnected (bool connected)
		{
			reportConnected = connected;
		}

		public void Apply ()
		{
			if (IsActive() == false || state == null)
			{
				return;
			}

			if (modelsSelected == false)
			{
				modelsSelected = SelectSingleModels();
			}

			if (leftSource != null)
			{
				bool leftGrab = state.LeftInput.IndexTrigger > 0.5f || state.LeftInput.HandTrigger > 0.5f;
				Pose leftPose = ResolveLocalPose(leftAnchor, state.LeftHandLocalPosition, state.LeftHandLocalRotation);
				WritePose(leftSource, leftPose.position, leftPose.rotation, leftGrab, state.LeftInput.IndexTrigger, state.LeftInput.HandTrigger);
			}

			if (rightSource != null)
			{
				bool rightGrab = state.RightInput.IndexTrigger > 0.5f || state.RightInput.HandTrigger > 0.5f;
				Pose rightPose = ResolveLocalPose(rightAnchor, state.RightHandLocalPosition, state.RightHandLocalRotation);
				WritePose(rightSource, rightPose.position, rightPose.rotation, rightGrab, state.RightInput.IndexTrigger, state.RightInput.HandTrigger);
			}

			ApplyControllerOffsets();
			ApplyPointerPoses();
			DriveModelAnimators();
		}

		// ControllerPointerPose only writes its transform from inside IController.WhenUpdated, and that
		// event fires from Controller.MarkInputDataRequiresUpdate - which dirties the data source first,
		// so the TryGetPointerPose call inside the handler pulls a fresh UpdateData() straight from the
		// native runtime and sees PointerPoseOrigin.None. Injecting into the data asset therefore never
		// reaches that handler no matter when we write it, and the ray origin, poke location and
		// distance-grab frustum stay frozen at the rig root. Drive their transforms directly instead,
		// the same way ControllerOffset is handled.
		private void ApplyPointerPoses ()
		{
			if (pointerPosesResolved == false)
			{
				ResolvePointerPoses();
				pointerPosesResolved = true;
			}

			DrivePointerPoses(leftPointerPoses, leftAnchor);
			DrivePointerPoses(rightPointerPoses, rightAnchor);
		}

		private void DrivePointerPoses (Component[] pointerPoses, Transform anchor)
		{
			if (pointerPoses == null || anchor == null)
			{
				return;
			}

			for (int i = 0; i < pointerPoses.Length; i++)
			{
				Component pointerPose = pointerPoses[i];

				if (pointerPose == null || pointerPose.gameObject.activeInHierarchy == false)
				{
					continue;
				}

				Vector3 localOffset = Vector3.zero;

				if (pointerPoseOffsetField != null)
				{
					localOffset = (Vector3)pointerPoseOffsetField.GetValue(pointerPose);
				}

				Vector3 worldPosition = anchor.position + anchor.rotation * localOffset;
				pointerPose.transform.SetPositionAndRotation(worldPosition, anchor.rotation);

				// The component's own handler leaves Active false because it never sees a valid pose;
				// any IActiveState gate watching it would otherwise read the interactor as inactive.
				if (pointerPoseActiveField != null)
				{
					pointerPoseActiveField.SetValue(pointerPose, BOXED_TRUE);
				}
			}
		}

		private void ResolvePointerPoses ()
		{
			Type pointerPoseType = Type.GetType("Oculus.Interaction.ControllerPointerPose, Oculus.Interaction");

			if (pointerPoseType == null)
			{
				return;
			}

			BindingFlags all = (BindingFlags)(0x4 | 0x8 | 0x10 | 0x20);
			pointerPoseOffsetField = pointerPoseType.GetField("_offset", all);
			pointerPoseActiveField = pointerPoseType.GetField("<Active>k__BackingField", all);

			UnityEngine.Object[] found = UnityEngine.Object.FindObjectsByType(pointerPoseType, FindObjectsInactive.Include, FindObjectsSortMode.None);

			System.Collections.Generic.List<Component> left = new System.Collections.Generic.List<Component>();
			System.Collections.Generic.List<Component> right = new System.Collections.Generic.List<Component>();

			for (int i = 0; i < found.Length; i++)
			{
				Component component = found[i] as Component;

				if (component == null || component.gameObject.scene.IsValid() == false)
				{
					continue;
				}

				if (ResolveIsLeft(component.transform) == true)
				{
					left.Add(component);
				}
				else
				{
					right.Add(component);
				}
			}

			leftPointerPoses = left.ToArray();
			rightPointerPoses = right.ToArray();
		}

		private void ApplyControllerOffsets ()
		{
			if (offsetsResolved == false)
			{
				ResolveOffsets();
				offsetsResolved = true;
			}

			DriveOffsets(leftOffsets, leftAnchor);
			DriveOffsets(rightOffsets, rightAnchor);
		}

		private void DriveOffsets (Component[] offsets, Transform anchor)
		{
			if (offsets == null || anchor == null)
			{
				return;
			}

			for (int i = 0; i < offsets.Length; i++)
			{
				Component offset = offsets[i];

				if (offset == null || offset.gameObject.activeInHierarchy == false)
				{
					continue;
				}

				Vector3 localOffset = Vector3.zero;
				Quaternion localRotation = Quaternion.identity;

				if (offsetOffsetField != null)
				{
					localOffset = (Vector3)offsetOffsetField.GetValue(offset);
				}

				if (offsetRotationField != null)
				{
					localRotation = (Quaternion)offsetRotationField.GetValue(offset);
				}

				Quaternion worldRotation = anchor.rotation * localRotation;
				Vector3 worldPosition = anchor.position + anchor.rotation * localOffset;
				offset.transform.SetPositionAndRotation(worldPosition, worldRotation);
			}
		}

		private void ResolveOffsets ()
		{
			Type offsetType = Type.GetType("Oculus.Interaction.ControllerOffset, Oculus.Interaction");

			if (offsetType == null)
			{
				return;
			}

			BindingFlags all = (BindingFlags)(0x4 | 0x8 | 0x10 | 0x20);
			offsetOffsetField = offsetType.GetField("_offset", all);
			offsetRotationField = offsetType.GetField("_rotation", all);

			UnityEngine.Object[] found = UnityEngine.Object.FindObjectsByType(offsetType, FindObjectsInactive.Include, FindObjectsSortMode.None);

			System.Collections.Generic.List<Component> left = new System.Collections.Generic.List<Component>();
			System.Collections.Generic.List<Component> right = new System.Collections.Generic.List<Component>();

			for (int i = 0; i < found.Length; i++)
			{
				Component component = found[i] as Component;

				if (component == null || component.gameObject.scene.IsValid() == false)
				{
					continue;
				}

				if (ResolveIsLeft(component.transform) == true)
				{
					left.Add(component);
				}
				else
				{
					right.Add(component);
				}
			}

			leftOffsets = left.ToArray();
			rightOffsets = right.ToArray();
		}

		private void DriveModelAnimators ()
		{
			if (animHashesReady == false)
			{
				animGripHash = Animator.StringToHash("Grip");
				animTriggerHash = Animator.StringToHash("Trigger");
				animButton1Hash = Animator.StringToHash("Button 1");
				animButton2Hash = Animator.StringToHash("Button 2");
				animHashesReady = true;
			}

			DriveAnimator(leftAnimator, state.LeftInput);
			DriveAnimator(rightAnimator, state.RightInput);
		}

		private void DriveAnimator (Animator animator, HandInputState input)
		{
			if (animator == null)
			{
				return;
			}

			animator.SetFloat(animGripHash, input.HandTrigger);
			animator.SetFloat(animTriggerHash, input.IndexTrigger);
			animator.SetFloat(animButton1Hash, input.PrimaryButton == true ? 1f : 0f);
			animator.SetFloat(animButton2Hash, input.SecondaryButton == true ? 1f : 0f);
		}

		// Returns true once model selection is settled (either every in-scene helper has been
		// pruned while active, or there is nothing to prune) so the caller can stop retrying.
		// Returns false while helpers still exist but are not active yet - the OVRComprehensive
		// interaction rig activates its OVRControllerVisualLeft/Right objects a few frames after
		// the sources resolve, so pruning on the very first Apply frame would find nothing and
		// leave every controller skin visible with no animator cached.
		private bool SelectSingleModels ()
		{
			modelSelectAttempts++;

			Type helperType = Type.GetType("OVRControllerHelper, Oculus.VR");

			if (helperType == null)
			{
				return true;
			}

			UnityEngine.Object[] helpers = UnityEngine.Object.FindObjectsByType(helperType, FindObjectsInactive.Include, FindObjectsSortMode.None);

			int sceneHelpers = 0;
			int processed = 0;

			for (int i = 0; i < helpers.Length; i++)
			{
				Component helper = helpers[i] as Component;

				if (helper == null || helper.gameObject.scene.IsValid() == false)
				{
					continue;
				}

				sceneHelpers++;

				if (helper.gameObject.activeInHierarchy == false)
				{
					continue;
				}

				SelectSingleModel(helperType, helper);
				processed++;
			}

			if (sceneHelpers == 0)
			{
				// This rig has no controller helper to manage; nothing to retry for.
				return true;
			}

			if (processed >= sceneHelpers)
			{
				return true;
			}

			// Some helper visuals are not active yet. Retry next frame, but give up after a
			// bounded number of attempts so a rig that keeps a controller visual permanently
			// inactive does not scan the scene every frame forever.
			return modelSelectAttempts >= MODEL_SELECT_MAX_ATTEMPTS;
		}

		private void SelectSingleModel (Type helperType, Component helper)
		{
			BindingFlags all = (BindingFlags)(0x4 | 0x8 | 0x10 | 0x20);
			bool isLeft = ResolveIsLeft(helper.transform);

			string preferredName = isLeft ? "m_modelMetaTouchPlusLeftController" : "m_modelMetaTouchPlusRightController";
			GameObject preferred = GetModelField(helperType, helper, all, preferredName);

			FieldInfo[] fields = helperType.GetFields(all);
			GameObject firstMatch = null;

			for (int i = 0; i < fields.Length; i++)
			{
				if (fields[i].FieldType != typeof(GameObject))
				{
					continue;
				}

				if (fields[i].Name.StartsWith("m_model") == false)
				{
					continue;
				}

				GameObject model = fields[i].GetValue(helper) as GameObject;

				if (model == null)
				{
					continue;
				}

				model.SetActive(false);

				bool fieldIsLeft = fields[i].Name.ToLower().Contains("left");

				if (fieldIsLeft == isLeft && firstMatch == null)
				{
					firstMatch = model;
				}
			}

			GameObject chosen = preferred != null ? preferred : firstMatch;

			if (chosen != null)
			{
				chosen.SetActive(true);

				Animator animator = chosen.GetComponentInChildren<Animator>(true);

				if (isLeft == true)
				{
					leftAnimator = animator;
				}
				else
				{
					rightAnimator = animator;
				}
			}
		}

		private bool ResolveIsLeft (Transform node)
		{
			Transform current = node;

			while (current != null)
			{
				string lower = current.name.ToLower();

				if (lower.Contains("left") == true)
				{
					return true;
				}

				if (lower.Contains("right") == true)
				{
					return false;
				}

				current = current.parent;
			}

			return false;
		}

		private GameObject GetModelField (Type helperType, Component helper, BindingFlags all, string fieldName)
		{
			FieldInfo field = helperType.GetField(fieldName, all);

			if (field == null)
			{
				return null;
			}

			return field.GetValue(helper) as GameObject;
		}

		private void WritePose (object source, Vector3 localPos, Quaternion localRot, bool grab, float trigger, float grip)
		{
			object asset = getDataMethod.Invoke(source, null);

			if (asset == null)
			{
				return;
			}

			WriteInput(asset, grab, trigger, grip);

			object pose = rootPoseField.GetValue(asset);
			pose = SetPose(pose, localPos, localRot);
			rootPoseField.SetValue(asset, pose);

			if (rootPoseOriginField != null)
			{
				rootPoseOriginField.SetValue(asset, ResolveBoxedPoseOrigin(rootPoseOriginField));
			}

			// The pointer (aim) pose is what ControllerPointerPose reads, and that component sits on
			// the ray interactor origin, the poke location and the distance-grab frustum/grab centre.
			// FromOVRControllerDataSource leaves PointerPoseOrigin at None without the native runtime,
			// so Controller.TryGetPointerPose fails and every one of those transforms stays frozen at
			// the rig root - the interactors run but cast from the wrong place. Reuse the root pose:
			// ControllerPointerPose applies its own prefab offset on top of it.
			if (pointerPoseField != null)
			{
				object pointerPose = pointerPoseField.GetValue(asset);
				pointerPose = SetPose(pointerPose, localPos, localRot);
				pointerPoseField.SetValue(asset, pointerPose);
			}

			if (pointerPoseOriginField != null)
			{
				pointerPoseOriginField.SetValue(asset, ResolveBoxedPoseOrigin(pointerPoseOriginField));
			}

			object connected = reportConnected == true ? BOXED_TRUE : BOXED_FALSE;

			if (isTrackedField != null)
			{
				isTrackedField.SetValue(asset, connected);
			}

			if (isConnectedField != null)
			{
				isConnectedField.SetValue(asset, connected);
			}

			if (isDataValidField != null)
			{
				isDataValidField.SetValue(asset, connected);
			}
		}

		private void ResolveTrackingTransform (BindingFlags all)
		{
			Type transformerType = Type.GetType("Oculus.Interaction.Input.TrackingToWorldTransformerOVR, Oculus.Interaction.OVR");

			if (transformerType == null)
			{
				return;
			}

			UnityEngine.Object[] transformers = UnityEngine.Object.FindObjectsByType(transformerType, FindObjectsInactive.Include, FindObjectsSortMode.None);

			for (int i = 0; i < transformers.Length; i++)
			{
				Component component = transformers[i] as Component;

				if (component == null || component.gameObject.activeInHierarchy == false)
				{
					continue;
				}

				PropertyInfo transformProp = transformerType.GetProperty("Transform", all);

				if (transformProp != null)
				{
					isdkTrackingTransform = transformProp.GetValue(component) as Transform;
				}

				if (isdkTrackingTransform != null)
				{
					return;
				}
			}
		}

		private Pose ResolveLocalPose (Transform anchor, Vector3 fallbackPos, Quaternion fallbackRot)
		{
			if (anchor == null || isdkTrackingTransform == null)
			{
				return new Pose(fallbackPos, fallbackRot);
			}

			Vector3 localPos = isdkTrackingTransform.InverseTransformPoint(anchor.position);
			Quaternion localRot = Quaternion.Inverse(isdkTrackingTransform.rotation) * anchor.rotation;

			return new Pose(localPos, localRot);
		}

		private void WriteInput (object asset, bool grab, float trigger, float grip)
		{
			if (inputField == null || buttonUsageMaskField == null)
			{
				return;
			}

			object input = inputField.GetValue(asset);

			int maskIndex = 0;

			if (grip > 0.5f)
			{
				maskIndex |= 1;
			}

			if (trigger > 0.5f)
			{
				maskIndex |= 2;
			}

			if (boxedUsageMasks != null)
			{
				buttonUsageMaskField.SetValue(input, boxedUsageMasks[maskIndex]);
			}

			if (triggerField != null)
			{
				triggerField.SetValue(input, trigger);
			}

			if (gripField != null)
			{
				gripField.SetValue(input, grip);
			}

			inputField.SetValue(asset, input);
		}

		private object ResolveBoxedPoseOrigin (FieldInfo originField)
		{
			if (boxedPoseOrigin == null && originField != null)
			{
				boxedPoseOrigin = Enum.ToObject(originField.FieldType, SYNTHETIC_POSE_ORIGIN);
			}

			return boxedPoseOrigin;
		}

		private object SetPose (object pose, Vector3 pos, Quaternion rot)
		{
			if (poseFieldPosition == null || poseFieldRotation == null)
			{
				Type poseType = pose.GetType();
				poseFieldPosition = poseType.GetField("position");
				poseFieldRotation = poseType.GetField("rotation");
			}

			if (poseFieldPosition != null)
			{
				poseFieldPosition.SetValue(pose, pos);
			}

			if (poseFieldRotation != null)
			{
				poseFieldRotation.SetValue(pose, rot);
			}

			return pose;
		}

		private void Resolve ()
		{
			resolved = true;

			Type sourceType = Type.GetType("Oculus.Interaction.Input.FromOVRControllerDataSource, Oculus.Interaction.OVR");

			if (sourceType == null)
			{
				resolveFailed = true;
				return;
			}

			UnityEngine.Object[] sources = UnityEngine.Object.FindObjectsByType(sourceType, FindObjectsInactive.Include, FindObjectsSortMode.None);

			if (sources == null || sources.Length == 0)
			{
				resolved = false;
				return;
			}

			BindingFlags all = (BindingFlags)(0x4 | 0x8 | 0x10 | 0x20);
			FieldInfo handednessField = sourceType.GetField("_handedness", all);
			getDataMethod = sourceType.GetMethod("GetData", all);

			for (int i = 0; i < sources.Length; i++)
			{
				object src = sources[i];
				Component component = src as Component;

				if (component != null && component.gameObject.activeInHierarchy == false)
				{
					continue;
				}

				object handedness = handednessField != null ? handednessField.GetValue(src) : null;
				string handStr = handedness != null ? handedness.ToString() : "";

				if (handStr == "Left")
				{
					leftSource = src;
				}
				else if (handStr == "Right")
				{
					rightSource = src;
				}
			}

			object probe = leftSource != null ? leftSource : rightSource;

			if (probe == null || getDataMethod == null)
			{
				resolveFailed = true;
				return;
			}

			object asset = getDataMethod.Invoke(probe, null);

			if (asset == null)
			{
				resolveFailed = true;
				return;
			}

			Type assetType = asset.GetType();
			rootPoseField = assetType.GetField("RootPose");
			rootPoseOriginField = assetType.GetField("RootPoseOrigin");
			pointerPoseField = assetType.GetField("PointerPose");
			pointerPoseOriginField = assetType.GetField("PointerPoseOrigin");
			isTrackedField = assetType.GetField("IsTracked");
			isConnectedField = assetType.GetField("IsConnected");
			isDataValidField = assetType.GetField("IsDataValid");
			inputField = assetType.GetField("Input");

			if (inputField != null)
			{
				Type inputType = inputField.FieldType;
				buttonUsageMaskField = inputType.GetField("<ButtonUsageMask>k__BackingField", all);
				triggerField = inputType.GetField("<Trigger>k__BackingField", all);
				gripField = inputType.GetField("<Grip>k__BackingField", all);

				buttonUsageType = Type.GetType("Oculus.Interaction.Input.ControllerButtonUsage, Oculus.Interaction");

				if (buttonUsageType != null)
				{
					// Precache the four possible boxed masks (indexed by grip bit 1 | trigger bit 2)
					// so WriteInput does not call Enum.ToObject (an allocation) per hand per frame.
					int gripButtonValue = Convert.ToInt32(Enum.Parse(buttonUsageType, "GripButton"));
					int triggerButtonValue = Convert.ToInt32(Enum.Parse(buttonUsageType, "TriggerButton"));
					boxedUsageMasks = new object[4];
					boxedUsageMasks[0] = Enum.ToObject(buttonUsageType, 0);
					boxedUsageMasks[1] = Enum.ToObject(buttonUsageType, gripButtonValue);
					boxedUsageMasks[2] = Enum.ToObject(buttonUsageType, triggerButtonValue);
					boxedUsageMasks[3] = Enum.ToObject(buttonUsageType, gripButtonValue | triggerButtonValue);
				}
			}

			ResolveTrackingTransform(all);

			if (rootPoseField == null)
			{
				resolveFailed = true;
			}
		}
	}
}
