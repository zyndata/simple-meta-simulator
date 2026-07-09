using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace SMS
{
	/// <summary>
	/// Feeds simulated controller poses into the Meta Interaction SDK (ISDK) controller data
	/// pipeline. ISDK reads controller poses from a native source (OVRInput.GetLocalControllerPosition)
	/// which returns zero without a headset, so the controller visuals sit at the origin. This hooks
	/// each FromOVRControllerDataSource's InputDataAvailable event and overwrites the ControllerDataAsset
	/// (RootPose, IsTracked, IsConnected) right after the source fills it, so visuals follow the
	/// simulated hands. All access is reflection-based; no reference to Oculus.Interaction is needed.
	/// </summary>
	public class ISDKControllerInjector
	{
		private const int SYNTHETIC_POSE_ORIGIN = 3;

		private bool resolved;
		private bool resolveFailed;
		private bool modelsSelected;

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
		private FieldInfo isTrackedField;
		private FieldInfo isConnectedField;
		private FieldInfo isDataValidField;
		private FieldInfo inputField;
		private FieldInfo buttonUsageMaskField;
		private FieldInfo triggerField;
		private FieldInfo gripField;
		private Type buttonUsageType;
		private int gripButtonValue;
		private int triggerButtonValue;
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

		public void Bind (SimulatedRigState rigState, Transform trackingSpaceTransform, Transform leftControllerAnchor, Transform rightControllerAnchor)
		{
			// A change of tracking space means the scene changed and the previously resolved
			// interaction sources belong to a destroyed rig. Force a full re-resolve.
			if (trackingSpace != trackingSpaceTransform)
			{
				resolved = false;
				resolveFailed = false;
				modelsSelected = false;
				offsetsResolved = false;
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

		public void Apply ()
		{
			if (IsActive() == false || state == null)
			{
				return;
			}

			if (modelsSelected == false)
			{
				SelectSingleModels();
				modelsSelected = true;
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
			DriveModelAnimators();
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

		private void SelectSingleModels ()
		{
			Type helperType = Type.GetType("OVRControllerHelper, Oculus.VR");

			if (helperType == null)
			{
				return;
			}

			UnityEngine.Object[] helpers = UnityEngine.Object.FindObjectsByType(helperType, FindObjectsInactive.Include, FindObjectsSortMode.None);

			for (int i = 0; i < helpers.Length; i++)
			{
				Component helper = helpers[i] as Component;

				if (helper == null || helper.gameObject.scene.IsValid() == false)
				{
					continue;
				}

				if (helper.gameObject.activeInHierarchy == false)
				{
					continue;
				}

				SelectSingleModel(helperType, helper);
			}
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
				if (boxedPoseOrigin == null)
				{
					boxedPoseOrigin = Enum.ToObject(rootPoseOriginField.FieldType, SYNTHETIC_POSE_ORIGIN);
				}

				rootPoseOriginField.SetValue(asset, boxedPoseOrigin);
			}

			if (isTrackedField != null)
			{
				isTrackedField.SetValue(asset, true);
			}

			if (isConnectedField != null)
			{
				isConnectedField.SetValue(asset, true);
			}

			if (isDataValidField != null)
			{
				isDataValidField.SetValue(asset, true);
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

			int maskValue = 0;

			if (grip > 0.5f)
			{
				maskValue |= gripButtonValue;
			}

			if (trigger > 0.5f)
			{
				maskValue |= triggerButtonValue;
			}

			if (buttonUsageType != null)
			{
				buttonUsageMaskField.SetValue(input, Enum.ToObject(buttonUsageType, maskValue));
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
					gripButtonValue = Convert.ToInt32(Enum.Parse(buttonUsageType, "GripButton"));
					triggerButtonValue = Convert.ToInt32(Enum.Parse(buttonUsageType, "TriggerButton"));
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
