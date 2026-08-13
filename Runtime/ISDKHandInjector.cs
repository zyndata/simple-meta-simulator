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
	/// Fingers are curled procedurally off the rig's own rest skeleton from the simulated trigger
	/// axes. All access is reflection-based; no reference to Oculus.Interaction is needed.
	/// </summary>
	public class ISDKHandInjector
	{
		private const int SYNTHETIC_POSE_ORIGIN = 3;
		private const int JOINT_COUNT = 26;
		private const int FINGER_COUNT = 5;
		private const float JOINT_RADIUS = 0.008f;
		private const float CURL_EPSILON = 0.001f;

		// OpenXR hand joint indices (Oculus.Interaction.Input.HandJointId): palm 0, wrist 1,
		// thumb 2-5, index 6-10, middle 11-15, ring 16-20, pinky 21-25, each finger running
		// metacarpal -> proximal -> intermediate -> distal -> tip.
		private static readonly int[] THUMB_JOINTS = { 2, 3, 4 };
		private static readonly int[] INDEX_JOINTS = { 7, 8, 9 };
		private static readonly int[] MIDDLE_JOINTS = { 12, 13, 14 };
		private static readonly int[] RING_JOINTS = { 17, 18, 19 };
		private static readonly int[] PINKY_JOINTS = { 22, 23, 24 };

		// Degrees of flexion at full curl, per joint of a finger (proximal, intermediate, distal).
		private static readonly float[] FINGER_CURL_ANGLES = { 60f, 80f, 55f };
		private static readonly float[] THUMB_CURL_ANGLES = { 20f, 35f, 40f };

		private static readonly object BOXED_TRUE = true;
		private static readonly object BOXED_FALSE = false;
		private static readonly object BOXED_HAND_SCALE = 1f;

		private bool resolved;
		private bool resolveFailed;

		private HandChannel left;
		private HandChannel right;

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
				left = null;
				right = null;
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
			return resolveFailed == false && (left != null || right != null);
		}

		public void Apply ()
		{
			if (IsActive() == false || state == null)
			{
				return;
			}

			WriteHand(left, leftAnchor, state.LeftInput);
			WriteHand(right, rightAnchor, state.RightInput);
		}

		private void WriteHand (HandChannel channel, Transform anchor, HandInputState input)
		{
			if (channel == null || channel.Asset == null)
			{
				return;
			}

			object asset = channel.Asset;

			if (channel.RestLocal == null)
			{
				ResolveRestPose(channel);
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

			// The joint arrays only change when the curl changes, so they are rewritten on demand
			// rather than every frame - nothing else touches them while the pipeline is frozen.
			if (HasCurlChanged(channel, input) == true)
			{
				WriteJoints(channel, input);
				WriteFingerState(asset, input);
				channel.LastIndexCurl = input.IndexTrigger;
				channel.LastHandCurl = input.HandTrigger;
				channel.JointsWritten = true;
			}

			PushCascade(channel.Source);
		}

		private bool HasCurlChanged (HandChannel channel, HandInputState input)
		{
			if (channel.JointsWritten == false)
			{
				return true;
			}

			return Mathf.Abs(channel.LastIndexCurl - input.IndexTrigger) > CURL_EPSILON
				|| Mathf.Abs(channel.LastHandCurl - input.HandTrigger) > CURL_EPSILON;
		}

		// Accumulates the skeleton's local-to-parent rest poses (plus the procedural curl) down the
		// parent chain into the root-relative poses JointPoses expects - FromOVRHandDataSource
		// writes Delta(Root, joint) there - and fills the legacy local-rotation array alongside them.
		private void WriteJoints (HandChannel channel, HandInputState input)
		{
			object asset = channel.Asset;
			Pose[] jointPoses = jointPosesField.GetValue(asset) as Pose[];
			Quaternion[] joints = jointsField.GetValue(asset) as Quaternion[];
			float[] radii = jointRadiiField.GetValue(asset) as float[];

			if (jointPoses == null)
			{
				return;
			}

			Pose[] restLocal = channel.RestLocal;
			int[] parents = channel.Parents;

			BuildCurl(channel, input);

			for (int i = 0; i < jointPoses.Length && i < restLocal.Length; i++)
			{
				int parent = parents[i];
				Quaternion localRotation = restLocal[i].rotation * channel.Curl[i];

				if (parent < 0 || parent >= i)
				{
					jointPoses[i] = Pose.identity;
				}
				else
				{
					Pose parentPose = jointPoses[parent];
					jointPoses[i] = new Pose(
						parentPose.position + parentPose.rotation * restLocal[i].position,
						parentPose.rotation * localRotation);
				}

				if (joints != null && i < joints.Length)
				{
					joints[i] = localRotation;
				}

				if (radii != null && i < radii.Length && radii[i] <= 0f)
				{
					radii[i] = JOINT_RADIUS;
				}
			}
		}

		// Flexion curls the finger toward the palm. In the OpenXR hand skeleton every joint has
		// its distal axis on +Z and its palmar axis on -Y (both hands), so bending the joint is a
		// positive rotation about its own X axis.
		private void BuildCurl (HandChannel channel, HandInputState input)
		{
			for (int i = 0; i < channel.Curl.Length; i++)
			{
				channel.Curl[i] = Quaternion.identity;
			}

			ApplyCurl(channel, THUMB_JOINTS, THUMB_CURL_ANGLES, input.HandTrigger);
			ApplyCurl(channel, INDEX_JOINTS, FINGER_CURL_ANGLES, input.IndexTrigger);
			ApplyCurl(channel, MIDDLE_JOINTS, FINGER_CURL_ANGLES, input.HandTrigger);
			ApplyCurl(channel, RING_JOINTS, FINGER_CURL_ANGLES, input.HandTrigger);
			ApplyCurl(channel, PINKY_JOINTS, FINGER_CURL_ANGLES, input.HandTrigger);
		}

		private void ApplyCurl (HandChannel channel, int[] jointIds, float[] angles, float amount)
		{
			float curl = Mathf.Clamp01(amount);

			if (curl <= 0f)
			{
				return;
			}

			for (int i = 0; i < jointIds.Length && i < angles.Length; i++)
			{
				int jointId = jointIds[i];

				if (jointId < 0 || jointId >= channel.Curl.Length)
				{
					continue;
				}

				channel.Curl[jointId] = Quaternion.Euler(angles[i] * curl, 0f, 0f);
			}
		}

		// Pinch is what the hand pinch selectors watch; drive it from the index trigger so the
		// same key that grabs with a controller also pinches with a hand.
		private void WriteFingerState (object asset, HandInputState input)
		{
			bool[] pinching = isFingerPinchingField.GetValue(asset) as bool[];
			bool[] confidence = isFingerHighConfidenceField.GetValue(asset) as bool[];
			float[] strength = fingerPinchStrengthField.GetValue(asset) as float[];
			bool isPinching = input.IndexTrigger > 0.5f;

			for (int i = 0; i < FINGER_COUNT; i++)
			{
				if (confidence != null && i < confidence.Length)
				{
					confidence[i] = true;
				}

				// Finger 0 is the thumb and finger 1 the index; a pinch is those two meeting.
				bool isPinchFinger = i <= 1;

				if (pinching != null && i < pinching.Length)
				{
					pinching[i] = isPinchFinger == true && isPinching == true;
				}

				if (strength != null && i < strength.Length)
				{
					strength[i] = isPinchFinger == true ? Mathf.Clamp01(input.IndexTrigger) : 0f;
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
		private void ResolveRestPose (HandChannel channel)
		{
			channel.RestLocal = new Pose[JOINT_COUNT];
			channel.Parents = new int[JOINT_COUNT];

			for (int i = 0; i < JOINT_COUNT; i++)
			{
				channel.RestLocal[i] = Pose.identity;
				channel.Parents[i] = -1;
			}

			if (configField == null)
			{
				return;
			}

			object config = configField.GetValue(channel.Asset);

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
				channel.RestLocal[i] = (Pose)poseField.GetValue(joint);
				channel.Parents[i] = (int)parentField.GetValue(joint);
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
					left = CreateChannel(source);
				}
				else if (handString == "Right")
				{
					right = CreateChannel(source);
				}
			}

			HandChannel probe = left != null ? left : right;

			if (probe == null || probe.Asset == null)
			{
				// The sources exist but are not active yet; retry on the next bind.
				resolved = false;
				left = null;
				right = null;
				return;
			}

			CacheAssetFields(probe.Asset.GetType());
			ResolveTrackingTransform(all);

			if (rootField == null || jointPosesField == null)
			{
				resolveFailed = true;
			}
		}

		private HandChannel CreateChannel (object source)
		{
			HandChannel channel = new HandChannel();
			channel.Source = source;
			channel.Asset = assetField.GetValue(source);
			channel.Curl = new Quaternion[JOINT_COUNT];

			for (int i = 0; i < JOINT_COUNT; i++)
			{
				channel.Curl[i] = Quaternion.identity;
			}

			return channel;
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

		private class HandChannel
		{
			public object Source;
			public object Asset;
			public Pose[] RestLocal;
			public int[] Parents;
			public Quaternion[] Curl;
			public bool JointsWritten;
			public float LastIndexCurl;
			public float LastHandCurl;
		}
	}
}
