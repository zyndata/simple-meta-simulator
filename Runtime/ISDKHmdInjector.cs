using System;
using System.Reflection;
using UnityEngine;

namespace SMS
{
	/// <summary>
	/// Feeds the simulated head pose into the Meta Interaction SDK (ISDK) HMD data pipeline.
	/// FromOVRHmdDataSource reads the center eye from the native runtime, so without a headset it
	/// reports IsTracked = false with an identity root, and - like the hand and controller sources -
	/// it is then never updated again, because OVRCameraRigRef dirties it from
	/// OVRCameraRig.UpdatedAnchors, an event the native runtime never fires.
	/// Everything that positions itself off the head therefore sits at the rig root: most visibly
	/// every CenterEyeOffset, which is the head frustum a distance grab candidate has to fall inside,
	/// so distance grab can never find a candidate no matter where the hands point.
	/// Each frame this writes the source's HmdDataAsset and pushes the update cascade by hand, the
	/// same recipe <see cref="ISDKHandInjector"/> uses. All access is reflection-based; no reference
	/// to Oculus.Interaction is needed.
	/// </summary>
	public class ISDKHmdInjector
	{
		private static readonly object BOXED_TRUE = true;
		private static readonly object BOXED_FALSE = false;

		private bool resolved;
		private bool resolveFailed;

		private object source;
		private object asset;

		private FieldInfo requiresUpdateField;
		private FieldInfo versionField;
		private FieldInfo eventField;
		private FieldInfo rootField;
		private FieldInfo isTrackedField;

		private Transform isdkTrackingTransform;
		private Transform centerEyeAnchor;
		private Transform trackingSpace;

		public void Bind (Transform trackingSpaceTransform, Transform headAnchor)
		{
			// A change of tracking space means the scene changed and the previously resolved source
			// belongs to a destroyed rig. Force a full re-resolve.
			if (trackingSpace != trackingSpaceTransform)
			{
				resolved = false;
				resolveFailed = false;
				source = null;
				asset = null;
			}

			trackingSpace = trackingSpaceTransform;
			centerEyeAnchor = headAnchor;

			if (resolved == false && resolveFailed == false)
			{
				Resolve();
			}
		}

		public bool IsActive ()
		{
			return resolveFailed == false && source != null && asset != null;
		}

		public void Apply ()
		{
			if (IsActive() == false || centerEyeAnchor == null)
			{
				return;
			}

			rootField.SetValue(asset, ResolveLocalPose(centerEyeAnchor));
			isTrackedField.SetValue(asset, BOXED_TRUE);

			// HmdDataAsset.FrameId is deliberately left alone: nothing downstream reads it, and
			// writing it would box an int every frame.
			PushCascade();
		}

		// MarkInputDataRequiresUpdate() minus the _requiresUpdate = true, so the consumers reached
		// inside the cascade (CenterEyeOffset moves its transform from Hmd.WhenUpdated) read the
		// injected asset instead of pulling the absent native runtime. See ISDKHandInjector.
		private void PushCascade ()
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
			if (isdkTrackingTransform == null)
			{
				return Pose.identity;
			}

			return new Pose(
				isdkTrackingTransform.InverseTransformPoint(anchor.position),
				Quaternion.Inverse(isdkTrackingTransform.rotation) * anchor.rotation);
		}

		private void Resolve ()
		{
			resolved = true;

			Type sourceType = Type.GetType("Oculus.Interaction.Input.FromOVRHmdDataSource, Oculus.Interaction.OVR");

			if (sourceType == null)
			{
				resolveFailed = true;
				return;
			}

			UnityEngine.Object[] sources = UnityEngine.Object.FindObjectsByType(sourceType, FindObjectsInactive.Include, FindObjectsSortMode.None);

			if (sources == null || sources.Length == 0)
			{
				// A rig with no ISDK HMD source at all; stay inert.
				resolveFailed = true;
				return;
			}

			BindingFlags all = (BindingFlags)(0x4 | 0x8 | 0x10 | 0x20);
			Type dataSourceType = sourceType.BaseType;

			FieldInfo assetField = sourceType.GetField("_hmdDataAsset", all);
			requiresUpdateField = dataSourceType != null ? dataSourceType.GetField("_requiresUpdate", all) : null;
			versionField = dataSourceType != null ? dataSourceType.GetField("_currentDataVersion", all) : null;
			eventField = dataSourceType != null ? dataSourceType.GetField("InputDataAvailable", all) : null;
			MethodInfo getDataMethod = sourceType.GetMethod("GetData", all);

			if (assetField == null || requiresUpdateField == null || versionField == null || eventField == null || getDataMethod == null)
			{
				resolveFailed = true;
				return;
			}

			for (int i = 0; i < sources.Length; i++)
			{
				Component component = sources[i] as Component;

				if (component != null && component.gameObject.activeInHierarchy == false)
				{
					continue;
				}

				source = sources[i];
				break;
			}

			if (source == null)
			{
				// The source exists but is not active yet; retry on the next bind.
				resolved = false;
				return;
			}

			// One real update so the source fills HmdDataAsset.Config: Hmd.TryGetRootPose reads
			// Config.TrackingToWorldTransformer and would throw on a config that was never built.
			getDataMethod.Invoke(source, null);

			asset = assetField.GetValue(source);

			if (asset == null)
			{
				resolveFailed = true;
				return;
			}

			Type assetType = asset.GetType();
			rootField = assetType.GetField("Root");
			isTrackedField = assetType.GetField("IsTracked");

			ResolveTrackingTransform(all);

			if (rootField == null || isTrackedField == null)
			{
				resolveFailed = true;
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
