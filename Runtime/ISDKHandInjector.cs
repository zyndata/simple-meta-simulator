using System;
using System.Reflection;
using UnityEngine;

namespace SMS
{
	/// <summary>
	/// Feeds simulated hand poses into the Meta Interaction SDK (ISDK) hand data pipeline, so the
	/// Hand interactor branches (hand grab, hand ray, hand poke, distance hand grab) come alive
	/// without a headset. ISDK sources hand data from OVRHand, which reports nothing without the
	/// native runtime, and the whole chain is then frozen: OVRCameraRigRef dirties the sources from
	/// OVRCameraRig.UpdatedAnchors, an event that never fires without the runtime, so
	/// FromOVRHandDataSource stays at data version 1 for the whole session.
	/// Each frame this writes the source's HandDataAsset and then pushes the update cascade by hand -
	/// bumping the data version and invoking InputDataAvailable while leaving the source marked
	/// clean, so every downstream pull reads the injected asset instead of re-pulling native data.
	/// All access is reflection-based; no reference to Oculus.Interaction is needed.
	/// </summary>
	public class ISDKHandInjector
	{
		private const int SYNTHETIC_POSE_ORIGIN = 3;
		private const int JOINT_COUNT = 26;

		private static readonly object BOXED_TRUE = true;
		private static readonly object BOXED_FALSE = false;
		private static readonly object BOXED_HAND_SCALE = 1f;

		private bool resolved;
		private bool resolveFailed;

		private object leftSource;
		private object rightSource;
		private object leftAsset;
		private object rightAsset;

		private FieldInfo assetField;
		private FieldInfo requiresUpdateField;
		private FieldInfo versionField;
		private FieldInfo eventField;

		private FieldInfo isDataValidField;
		private FieldInfo isConnectedField;
		private FieldInfo isTrackedField;
		private FieldInfo isHighConfidenceField;
		private FieldInfo rootField;
		private FieldInfo rootPoseOriginField;
		private FieldInfo pointerPoseField;
		private FieldInfo pointerPoseOriginField;
		private FieldInfo jointPosesField;
		private FieldInfo jointRadiiField;
		private FieldInfo jointsField;
		private FieldInfo handScaleField;
		private FieldInfo isFingerPinchingField;
		private FieldInfo isFingerHighConfidenceField;
		private FieldInfo fingerPinchStrengthField;
		private FieldInfo configField;
		private object boxedPoseOrigin;

		private Pose[] leftRestLocal;
		private Pose[] rightRestLocal;
		private int[] leftParents;
		private int[] rightParents;
		private bool leftJointsWritten;
		private bool rightJointsWritten;

		private SimulatedRigState state;
		private Transform isdkTrackingTransform;
		private Transform leftAnchor;
		private Transform rightAnchor;
		private Transform trackingSpace;

		public void Bind (SimulatedRigState rigState, Transform trackingSpaceTransform, Transform leftHandAnchor, Transform rightHandAnchor)
		{
			// A change of tracking space means the scene changed and the previously resolved
			// sources belong to a destroyed rig. Force a full re-resolve.
			if (trackingSpace != trackingSpaceTransform)
			{
				resolved = false;
				resolveFailed = false;
				leftSource = null;
				rightSource = null;
				leftAsset = null;
				rightAsset = null;
				leftRestLocal = null;
				rightRestLocal = null;
				leftJointsWritten = false;
				rightJointsWritten = false;
			}

			state = rigState;
			trackingSpace = trackingSpaceTransform;
			leftAnchor = leftHandAnchor;
			rightAnchor = rightHandAnchor;

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

			if (leftSource != null)
			{
				leftJointsWritten = WriteHand(leftSource, leftAsset, leftAnchor, ref leftRestLocal, ref leftParents, leftJointsWritten);
			}

			if (rightSource != null)
			{
				rightJointsWritten = WriteHand(rightSource, rightAsset, rightAnchor, ref rightRestLocal, ref rightParents, rightJointsWritten);
			}
		}

		private bool WriteHand (object source, object asset, Transform anchor, ref Pose[] restLocal, ref int[] parents, bool jointsWritten)
		{
			if (asset == null)
			{
				return jointsWritten;
			}

			if (restLocal == null)
			{
				ResolveRestPose(asset, ref restLocal, ref parents);
			}

			Pose root = ResolveLocalPose(anchor);

			isDataValidField.SetValue(asset, BOXED_TRUE);
			isConnectedField.SetValue(asset, BOXED_TRUE);
			isTrackedField.SetValue(asset, BOXED_TRUE);
			isHighConfidenceField.SetValue(asset, BOXED_TRUE);
			handScaleField.SetValue(asset, BOXED_HAND_SCALE);
			rootField.SetValue(asset, root);
			rootPoseOriginField.SetValue(asset, boxedPoseOrigin);
			pointerPoseField.SetValue(asset, root);
			pointerPoseOriginField.SetValue(asset, boxedPoseOrigin);

			// The joint arrays only change when the pose changes, so they are written once and
			// then left alone - nothing else touches them while the pipeline is frozen.
			if (jointsWritten == false)
			{
				WriteJoints(asset, restLocal, parents);
				jointsWritten = true;
			}

			PushCascade(source);
			return jointsWritten;
		}

		// Accumulates the skeleton's local-to-parent rest poses down the parent chain into the
		// root-relative poses JointPoses expects (FromOVRHandDataSource writes Delta(Root, joint)),
		// and fills the legacy local-rotation array alongside them.
		private void WriteJoints (object asset, Pose[] restLocal, int[] parents)
		{
			Pose[] jointPoses = jointPosesField.GetValue(asset) as Pose[];
			Quaternion[] joints = jointsField.GetValue(asset) as Quaternion[];
			float[] radii = jointRadiiField.GetValue(asset) as float[];

			if (jointPoses == null)
			{
				return;
			}

			for (int i = 0; i < jointPoses.Length && i < restLocal.Length; i++)
			{
				int parent = parents[i];

				if (parent < 0 || parent >= i)
				{
					jointPoses[i] = Pose.identity;
				}
				else
				{
					Pose parentPose = jointPoses[parent];
					jointPoses[i] = new Pose(
						parentPose.position + parentPose.rotation * restLocal[i].position,
						parentPose.rotation * restLocal[i].rotation);
				}

				if (joints != null && i < joints.Length)
				{
					joints[i] = restLocal[i].rotation;
				}

				if (radii != null && i < radii.Length && radii[i] <= 0f)
				{
					radii[i] = 0.008f;
				}
			}

			bool[] fingerConfidence = isFingerHighConfidenceField.GetValue(asset) as bool[];

			if (fingerConfidence != null)
			{
				for (int i = 0; i < fingerConfidence.Length; i++)
				{
					fingerConfidence[i] = true;
				}
			}
		}

		// MarkInputDataRequiresUpdate() minus the _requiresUpdate = true. Dirtying the source the
		// normal way would make every downstream pull inside the cascade run UpdateData() on it,
		// which re-reads the (absent) native runtime and throws the injected data away.
		private void PushCascade (object source)
		{
			requiresUpdateField.SetValue(source, BOXED_FALSE);
			versionField.SetValue(source, (int)versionField.GetValue(source) + 1);

			Action available = eventField.GetValue(source) as Action;

			if (available != null)
			{
				available();
			}
		}

		private Pose ResolveLocalPose (Transform anchor)
		{
			if (anchor == null || isdkTrackingTransform == null)
			{
				return Pose.identity;
			}

			return new Pose(
				isdkTrackingTransform.InverseTransformPoint(anchor.position),
				Quaternion.Inverse(isdkTrackingTransform.rotation) * anchor.rotation);
		}

		// The rest skeleton is read off the asset's own Config, which HandSkeletonOVR fills from
		// baked OVRSkeletonData - so it needs no headset and adapts to whatever skeleton the rig uses.
		private void ResolveRestPose (object asset, ref Pose[] restLocal, ref int[] parents)
		{
			restLocal = new Pose[JOINT_COUNT];
			parents = new int[JOINT_COUNT];

			for (int i = 0; i < JOINT_COUNT; i++)
			{
				restLocal[i] = Pose.identity;
				parents[i] = -1;
			}

			if (configField == null)
			{
				return;
			}

			object config = configField.GetValue(asset);

			if (config == null)
			{
				return;
			}

			BindingFlags all = (BindingFlags)(0x4 | 0x8 | 0x10 | 0x20);
			PropertyInfo skeletonProperty = config.GetType().GetProperty("HandSkeleton", all);
			object skeleton = skeletonProperty != null ? skeletonProperty.GetValue(config) : null;

			if (skeleton == null)
			{
				return;
			}

			FieldInfo skeletonJointsField = skeleton.GetType().GetField("joints");
			Array joints = skeletonJointsField != null ? skeletonJointsField.GetValue(skeleton) as Array : null;

			if (joints == null)
			{
				return;
			}

			Type jointType = joints.GetType().GetElementType();
			FieldInfo poseField = jointType.GetField("pose");
			FieldInfo parentField = jointType.GetField("parent");

			if (poseField == null || parentField == null)
			{
				return;
			}

			for (int i = 0; i < JOINT_COUNT && i < joints.Length; i++)
			{
				object joint = joints.GetValue(i);
				restLocal[i] = (Pose)poseField.GetValue(joint);
				parents[i] = (int)parentField.GetValue(joint);
			}
		}

		private void Resolve ()
		{
			resolved = true;

			Type sourceType = Type.GetType("Oculus.Interaction.Input.FromOVRHandDataSource, Oculus.Interaction.OVR");

			if (sourceType == null)
			{
				resolveFailed = true;
				return;
			}

			UnityEngine.Object[] sources = UnityEngine.Object.FindObjectsByType(sourceType, FindObjectsInactive.Include, FindObjectsSortMode.None);

			if (sources == null || sources.Length == 0)
			{
				// A rig with no hand data source at all (controller-only rigs); stay inert.
				resolveFailed = true;
				return;
			}

			BindingFlags all = (BindingFlags)(0x4 | 0x8 | 0x10 | 0x20);
			Type dataSourceType = sourceType.BaseType;

			assetField = sourceType.GetField("_handDataAsset", all);
			requiresUpdateField = dataSourceType != null ? dataSourceType.GetField("_requiresUpdate", all) : null;
			versionField = dataSourceType != null ? dataSourceType.GetField("_currentDataVersion", all) : null;
			eventField = dataSourceType != null ? dataSourceType.GetField("InputDataAvailable", all) : null;

			if (assetField == null || requiresUpdateField == null || versionField == null || eventField == null)
			{
				resolveFailed = true;
				return;
			}

			FieldInfo handednessField = sourceType.GetField("_handedness", all);

			for (int i = 0; i < sources.Length; i++)
			{
				object source = sources[i];
				Component component = source as Component;

				if (component != null && component.gameObject.activeInHierarchy == false)
				{
					continue;
				}

				object handedness = handednessField != null ? handednessField.GetValue(source) : null;
				string handString = handedness != null ? handedness.ToString() : "";

				if (handString == "Left")
				{
					leftSource = source;
					leftAsset = assetField.GetValue(source);
				}
				else if (handString == "Right")
				{
					rightSource = source;
					rightAsset = assetField.GetValue(source);
				}
			}

			object probe = leftAsset != null ? leftAsset : rightAsset;

			if (probe == null)
			{
				// The sources exist but are not active yet; retry on the next bind.
				resolved = false;
				return;
			}

			CacheAssetFields(probe.GetType());
			ResolveTrackingTransform(all);

			if (rootField == null || jointPosesField == null)
			{
				resolveFailed = true;
			}
		}

		private void CacheAssetFields (Type assetType)
		{
			isDataValidField = assetType.GetField("IsDataValid");
			isConnectedField = assetType.GetField("IsConnected");
			isTrackedField = assetType.GetField("IsTracked");
			isHighConfidenceField = assetType.GetField("IsHighConfidence");
			rootField = assetType.GetField("Root");
			rootPoseOriginField = assetType.GetField("RootPoseOrigin");
			pointerPoseField = assetType.GetField("PointerPose");
			pointerPoseOriginField = assetType.GetField("PointerPoseOrigin");
			jointPosesField = assetType.GetField("JointPoses");
			jointRadiiField = assetType.GetField("JointRadii");
			jointsField = assetType.GetField("Joints");
			handScaleField = assetType.GetField("HandScale");
			isFingerPinchingField = assetType.GetField("IsFingerPinching");
			isFingerHighConfidenceField = assetType.GetField("IsFingerHighConfidence");
			fingerPinchStrengthField = assetType.GetField("FingerPinchStrength");
			configField = assetType.GetField("Config");

			if (rootPoseOriginField != null)
			{
				boxedPoseOrigin = Enum.ToObject(rootPoseOriginField.FieldType, SYNTHETIC_POSE_ORIGIN);
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

				PropertyInfo transformProperty = transformerType.GetProperty("Transform", all);

				if (transformProperty != null)
				{
					isdkTrackingTransform = transformProperty.GetValue(component) as Transform;
				}

				if (isdkTrackingTransform != null)
				{
					return;
				}
			}
		}
	}
}
