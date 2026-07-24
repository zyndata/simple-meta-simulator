using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SMS.Editor
{
	/// <summary>
	/// Settings window for the Simple Meta Simulator, opened from the toolbar status button.
	/// Exposes the enable toggle, movement / look / hand tuning, the hand activation mode, and the
	/// OVR event toggles. Uses plain IMGUI so the package carries no Odin dependency. The input
	/// bindings button creates the controls asset on demand, then opens it for editing.
	/// </summary>
	public class SimulatorSettingsWindow : EditorWindow
	{
		private const string WINDOW_TITLE = "Simple Meta Simulator";
		private const float LABEL_WIDTH = 190f;

		private Vector2 scroll;
		private OVREventInvoker eventInvoker;

		private bool movementFoldout = false;
		private bool lookFoldout = false;
		private bool handsFoldout = false;
		private bool eventsFoldout = false;

		public static void Open ()
		{
			SimulatorSettingsWindow window = GetWindow<SimulatorSettingsWindow>(false, WINDOW_TITLE, true);
			window.minSize = new Vector2(360f, 440f);
			window.Show();
		}

		private void OnGUI ()
		{
			SimulatorConfig config = SimulatorSettings.Config;

			EditorGUIUtility.labelWidth = LABEL_WIDTH;
			EditorGUILayout.Space();

			scroll = EditorGUILayout.BeginScrollView(scroll);

			EditorGUI.BeginChangeCheck();

			DrawGeneral(config);
			DrawMovement(config);
			DrawLook(config);
			DrawHands(config);
			DrawEvents(config);

			if (EditorGUI.EndChangeCheck() == true)
			{
				SimulatorSettings.Save();
			}

			EditorGUILayout.EndScrollView();

			DrawFooter();
		}

		private void DrawGeneral (SimulatorConfig config)
		{
			EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
			config.Enabled = EditorGUILayout.Toggle("Enabled", config.Enabled);
			EditorGUILayout.HelpBox("When enabled and no headset is present, play mode spawns the in-editor simulator.", MessageType.None);
			EditorGUILayout.Space();
		}

		private void DrawMovement (SimulatorConfig config)
		{
			movementFoldout = EditorGUILayout.Foldout(movementFoldout, "Movement", true);

			if (movementFoldout == true)
			{
				config.MoveSpeed = EditorGUILayout.FloatField("Move Speed", config.MoveSpeed);
				config.VerticalSpeed = EditorGUILayout.FloatField("Vertical Speed", config.VerticalSpeed);
				config.StartingEyeHeight = EditorGUILayout.FloatField("Starting Eye Height", config.StartingEyeHeight);
			}

			EditorGUILayout.Space();
		}

		private void DrawLook (SimulatorConfig config)
		{
			lookFoldout = EditorGUILayout.Foldout(lookFoldout, "Look", true);

			if (lookFoldout == true)
			{
				config.LookSensitivity = EditorGUILayout.FloatField("Look Sensitivity", config.LookSensitivity);
				config.InvertLookY = EditorGUILayout.Toggle("Invert Look Y", config.InvertLookY);
			}

			EditorGUILayout.Space();
		}

		private void DrawHands (SimulatorConfig config)
		{
			EditorGUILayout.HelpBox("Press Tab to cycle the movement target: Both -> Left hand -> Right hand -> Head. WASD/QE moves the active target, mouse + right button looks around. In the Left or Right target, hold the middle mouse button and move the mouse to rotate that hand/controller.", MessageType.None);

			if (Application.isPlaying == true && InEditorXRSimulator.Active != null)
			{
				EditorGUILayout.LabelField("Current Target", InEditorXRSimulator.Active.CurrentCycleTargetName);
				Repaint();
			}

			handsFoldout = EditorGUILayout.Foldout(handsFoldout, "Hands", true);

			if (handsFoldout == true)
			{
				config.HandRotateSensitivity = EditorGUILayout.FloatField("Hand Rotate Sensitivity", config.HandRotateSensitivity);
				config.InvertHandRotateY = EditorGUILayout.Toggle("Invert Hand Rotate Y", config.InvertHandRotateY);
				config.GrabGripInputMode = (ButtonInputMode)EditorGUILayout.EnumPopup("Grab/Grip Mode", config.GrabGripInputMode);
				config.FaceButtonInputMode = (ButtonInputMode)EditorGUILayout.EnumPopup("Face Button Mode", config.FaceButtonInputMode);
				EditorGUILayout.HelpBox("Held: the input is active only while its key is held. Toggle: each key press latches it until pressed again. Grab/Grip covers the index and hand triggers; Face Button covers X/Y/A/B.", MessageType.None);
			}

			EditorGUILayout.Space();
		}

		private void DrawEvents (SimulatorConfig config)
		{
			eventsFoldout = EditorGUILayout.Foldout(eventsFoldout, "OVR Events", true);

			if (eventsFoldout == true)
			{
				config.RaiseHmdMountedEvents = EditorGUILayout.Toggle("HMD Mounted / Unmounted", config.RaiseHmdMountedEvents);
				config.RaiseInputFocusEvents = EditorGUILayout.Toggle("Input Focus", config.RaiseInputFocusEvents);
				config.RaiseTrackingEvents = EditorGUILayout.Toggle("Tracking", config.RaiseTrackingEvents);
				config.RaiseHmdAcquiredEvents = EditorGUILayout.Toggle("HMD Acquired / Lost", config.RaiseHmdAcquiredEvents);
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Invoke Events (play mode)", EditorStyles.miniBoldLabel);

			using (new EditorGUI.DisabledScope(Application.isPlaying == false))
			{
				DrawInvokeRow("HMD Mounted", "HMDMounted", "HMD Unmounted", "HMDUnmounted");
				DrawInvokeRow("Input Focus Acquired", "InputFocusAcquired", "Input Focus Lost", "InputFocusLost");
				DrawInvokeRow("Tracking Acquired", "TrackingAcquired", "Tracking Lost", "TrackingLost");
				DrawInvokeRow("HMD Acquired", "HMDAcquired", "HMD Lost", "HMDLost");
			}

			EditorGUILayout.Space();
		}

		private void DrawInvokeRow (string labelA, string eventA, string labelB, string eventB)
		{
			const float SPACING = 4f;

			Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4f);
			float half = (row.width - SPACING) * 0.5f;
			Rect left = new Rect(row.x, row.y, half, row.height);
			Rect right = new Rect(row.x + half + SPACING, row.y, half, row.height);

			if (GUI.Button(left, labelA) == true)
			{
				Invoke(eventA);
			}

			if (GUI.Button(right, labelB) == true)
			{
				Invoke(eventB);
			}
		}

		private void Invoke (string eventName)
		{
			if (eventInvoker == null)
			{
				eventInvoker = new OVREventInvoker();
			}

			eventInvoker.RaiseSingle(eventName);
			Debug.Log("[SMS] Invoked OVR event: " + eventName + " (current state: " + eventName + " raised)");
		}

		private void DrawFooter ()
		{
			EditorGUILayout.Space();

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Edit Input Bindings", GUILayout.Height(24f)) == true)
				{
					OpenOrCreateControlsAsset();
				}

				if (GUILayout.Button("Reset To Defaults", GUILayout.Height(24f)) == true)
				{
					ResetToDefaults();
				}
			}
		}

		private void OpenOrCreateControlsAsset ()
		{
			InputActionAsset asset = SimulatorSettings.LoadControlsAsset();

			if (asset == null)
			{
				asset = CreateControlsAsset();
			}

			if (asset == null)
			{
				return;
			}

			EditorGUIUtility.PingObject(asset);
			Selection.activeObject = asset;
			AssetDatabase.OpenAsset(asset);
		}

		private InputActionAsset CreateControlsAsset ()
		{
			SimulatorInputActions defaults = new SimulatorInputActions(null);
			string json = defaults.Asset.ToJson();
			string path = SimulatorSettings.ControlsAssetPath;

			File.WriteAllText(path, json);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
			return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
		}

		private void ResetToDefaults ()
		{
			bool confirmed = EditorUtility.DisplayDialog(WINDOW_TITLE, "Reset all simulator settings to their defaults?", "Reset", "Cancel");

			if (confirmed == false)
			{
				return;
			}

			SimulatorSettings.ReplaceConfig(new SimulatorConfig());
			Repaint();
		}

	}
}
