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

		private bool movementFoldout = true;
		private bool lookFoldout = true;
		private bool handsFoldout = true;
		private bool eventsFoldout = true;

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
				SetFloat(config, "moveSpeed", EditorGUILayout.FloatField("Move Speed", GetFloat(config, "moveSpeed")));
				SetFloat(config, "verticalSpeed", EditorGUILayout.FloatField("Vertical Speed", GetFloat(config, "verticalSpeed")));
				SetFloat(config, "startingEyeHeight", EditorGUILayout.FloatField("Starting Eye Height", GetFloat(config, "startingEyeHeight")));
			}

			EditorGUILayout.Space();
		}

		private void DrawLook (SimulatorConfig config)
		{
			lookFoldout = EditorGUILayout.Foldout(lookFoldout, "Look", true);

			if (lookFoldout == true)
			{
				SetFloat(config, "lookSensitivity", EditorGUILayout.FloatField("Look Sensitivity", GetFloat(config, "lookSensitivity")));
				SetBool(config, "invertLookY", EditorGUILayout.Toggle("Invert Look Y", GetBool(config, "invertLookY")));
			}

			EditorGUILayout.Space();
		}

		private void DrawHands (SimulatorConfig config)
		{
			EditorGUILayout.HelpBox("Press Tab to cycle the movement target: Both -> Left hand -> Right hand -> Head. WASD/QE moves the active target, mouse + right button looks around.", MessageType.None);

			if (Application.isPlaying == true && InEditorXRSimulator.Active != null)
			{
				EditorGUILayout.LabelField("Current Target", InEditorXRSimulator.Active.CurrentCycleTargetName);
				Repaint();
			}

			handsFoldout = EditorGUILayout.Foldout(handsFoldout, "Hands", true);

			if (handsFoldout == true)
			{
				SetFloat(config, "handMoveSpeed", EditorGUILayout.FloatField("Hand Move Speed", GetFloat(config, "handMoveSpeed")));
				SetFloat(config, "handDepthSpeed", EditorGUILayout.FloatField("Hand Depth Speed", GetFloat(config, "handDepthSpeed")));
				SetFaceButtonMode(config, (ButtonInputMode)EditorGUILayout.EnumPopup("Face Button Mode", config.FaceButtonInputMode));
				EditorGUILayout.HelpBox("Held: X/Y/A/B are pressed only while their key is held. Toggle: each key press latches the button until pressed again.", MessageType.None);
			}

			EditorGUILayout.Space();
		}

		private void DrawEvents (SimulatorConfig config)
		{
			eventsFoldout = EditorGUILayout.Foldout(eventsFoldout, "OVR Events", true);

			if (eventsFoldout == true)
			{
				SetBool(config, "raiseHmdMountedEvents", EditorGUILayout.Toggle("HMD Mounted / Unmounted", GetBool(config, "raiseHmdMountedEvents")));
				SetBool(config, "raiseInputFocusEvents", EditorGUILayout.Toggle("Input Focus", GetBool(config, "raiseInputFocusEvents")));
				SetBool(config, "raiseTrackingEvents", EditorGUILayout.Toggle("Tracking", GetBool(config, "raiseTrackingEvents")));
				SetBool(config, "raiseHmdAcquiredEvents", EditorGUILayout.Toggle("HMD Acquired / Lost", GetBool(config, "raiseHmdAcquiredEvents")));
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
			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button(labelA) == true)
				{
					Invoke(eventA);
				}

				if (GUILayout.Button(labelB) == true)
				{
					Invoke(eventB);
				}
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

		private static float GetFloat (SimulatorConfig config, string field)
		{
			var f = typeof(SimulatorConfig).GetField(field, (System.Reflection.BindingFlags)(0x4 | 0x20));
			return f != null ? (float)f.GetValue(config) : 0f;
		}

		private static void SetFloat (SimulatorConfig config, string field, float value)
		{
			var f = typeof(SimulatorConfig).GetField(field, (System.Reflection.BindingFlags)(0x4 | 0x20));

			if (f != null)
			{
				f.SetValue(config, value);
			}
		}

		private static bool GetBool (SimulatorConfig config, string field)
		{
			var f = typeof(SimulatorConfig).GetField(field, (System.Reflection.BindingFlags)(0x4 | 0x20));
			return f != null && (bool)f.GetValue(config);
		}

		private static void SetBool (SimulatorConfig config, string field, bool value)
		{
			var f = typeof(SimulatorConfig).GetField(field, (System.Reflection.BindingFlags)(0x4 | 0x20));

			if (f != null)
			{
				f.SetValue(config, value);
			}
		}

		private static void SetHandMode (SimulatorConfig config, HandActivationMode value)
		{
			var f = typeof(SimulatorConfig).GetField("handActivationMode", (System.Reflection.BindingFlags)(0x4 | 0x20));

			if (f != null)
			{
				f.SetValue(config, value);
			}
		}

		private static void SetFaceButtonMode (SimulatorConfig config, ButtonInputMode value)
		{
			var f = typeof(SimulatorConfig).GetField("faceButtonInputMode", (System.Reflection.BindingFlags)(0x4 | 0x20));

			if (f != null)
			{
				f.SetValue(config, value);
			}
		}
	}
}
