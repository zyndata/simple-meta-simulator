using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SMS.Editor
{
	/// <summary>
	/// Editor-side owner of the simulator configuration. Persists the config to EditorPrefs as JSON
	/// (never shipped in a build), loads the optional input bindings asset from the package by path,
	/// and pushes both into the runtime layer before play mode via [InitializeOnLoad].
	/// </summary>
	[InitializeOnLoad]
	public static class SimulatorSettings
	{
		private const string PREFS_KEY = "dev.gorny.sms.config";
		private const string CONTROLS_ASSET_PATH = "Packages/dev.gorny.sms/Editor/SimulatorControls.inputactions";

		private static SimulatorConfig cachedConfig;

		static SimulatorSettings ()
		{
			PushToRuntime();
		}

		public static SimulatorConfig Config
		{
			get
			{
				if (cachedConfig == null)
				{
					cachedConfig = LoadOrCreate();
				}

				return cachedConfig;
			}
		}

		public static bool IsEnabled ()
		{
			return Config.Enabled;
		}

		public static void SetEnabled (bool value)
		{
			Config.Enabled = value;
			Save();
		}

		public static void Save ()
		{
			string json = JsonUtility.ToJson(Config);
			EditorPrefs.SetString(PREFS_KEY, json);
			PushToRuntime();
		}

		public static void ReplaceConfig (SimulatorConfig fresh)
		{
			cachedConfig = fresh;
			Save();
		}

		public static InputActionAsset LoadControlsAsset ()
		{
			return AssetDatabase.LoadAssetAtPath<InputActionAsset>(CONTROLS_ASSET_PATH);
		}

		public static string ControlsAssetPath => CONTROLS_ASSET_PATH;

		private static void PushToRuntime ()
		{
			SimulatorConfigProvider.Current = Config;
			SimulatorConfigProvider.Controls = LoadControlsAsset();
		}

		private static SimulatorConfig LoadOrCreate ()
		{
			SimulatorConfig config = new SimulatorConfig();

			if (EditorPrefs.HasKey(PREFS_KEY) == true)
			{
				try
				{
					JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(PREFS_KEY), config);
				}
				catch
				{
					config = new SimulatorConfig();
				}
			}

			return config;
		}
	}
}
