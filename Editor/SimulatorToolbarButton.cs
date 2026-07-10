using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace SMS.Editor
{
	/// <summary>
	/// Injects a status toggle into the Unity 6 main toolbar, to the right of the Play button,
	/// showing whether the simulator is enabled. Clicking opens the simulator settings window.
	/// Injects directly into the live toolbar visual tree to guarantee the icon shows.
	/// </summary>
	[InitializeOnLoad]
	internal static class SimulatorToolbarButton
	{
		private const string BUTTON_NAME = "SMSSimulatorToolbarButton";
		private const string MIDDLE_CONTAINER_CLASS = "unity-overlay-container__middle-container";
		private const string META_OVERLAY_NAME = "MetaXR/PlayCompanion";
		private const string TOOLBAR_WINDOW_TYPE = "UnityEditor.MainToolbarWindow";

		private const string ACTIVE_ICON = "greenLight";
		private const string BLOCKED_ICON = "orangeLight";
		private const string INACTIVE_ICON = "lightOff";
		private const float ICON_SIZE = 16f;

		private const double REFRESH_INTERVAL = 0.5d;

		private static EditorToolbarButton button;
		private static double nextRefreshTime;
		private static ToolbarState lastKnownState;
		private static bool hasCachedState;

		static SimulatorToolbarButton ()
		{
			EditorApplication.update += OnEditorUpdate;
		}

		private static void OnEditorUpdate ()
		{
			if (EditorApplication.timeSinceStartup < nextRefreshTime)
			{
				return;
			}

			nextRefreshTime = EditorApplication.timeSinceStartup + REFRESH_INTERVAL;

			EnsureInjected();

			if (button == null)
			{
				return;
			}

			ToolbarState state = ResolveState();

			if (hasCachedState == true && state == lastKnownState)
			{
				return;
			}

			lastKnownState = state;
			hasCachedState = true;
			ApplyVisualState(state);
		}

		private static ToolbarState ResolveState ()
		{
			if (SimulatorSettings.IsEnabled() == false)
			{
				return ToolbarState.Disabled;
			}

			if (SimulatorBootstrapper.IsRealHeadsetPresent() == true)
			{
				return ToolbarState.Blocked;
			}

			return ToolbarState.Enabled;
		}

		private static void EnsureInjected ()
		{
			if (button != null && button.panel != null)
			{
				return;
			}

			EditorWindow toolbarWindow = FindToolbarWindow();

			if (toolbarWindow == null)
			{
				return;
			}

			VisualElement root = toolbarWindow.rootVisualElement;

			if (root == null)
			{
				return;
			}

			VisualElement existing = root.Q<VisualElement>(BUTTON_NAME);

			if (existing != null)
			{
				button = existing as EditorToolbarButton;
				return;
			}

			VisualElement middleContainer = root.Q(className: MIDDLE_CONTAINER_CLASS);

			if (middleContainer == null)
			{
				return;
			}

			button = CreateButton();

			VisualElement metaOverlay = root.Q(META_OVERLAY_NAME);

			if (metaOverlay != null && metaOverlay.parent == middleContainer)
			{
				int metaIndex = middleContainer.IndexOf(metaOverlay);
				middleContainer.Insert(metaIndex + 1, button);
			}
			else
			{
				middleContainer.Add(button);
			}

			hasCachedState = false;
			ApplyVisualState(ResolveState());
		}

		private static EditorToolbarButton CreateButton ()
		{
			EditorToolbarButton created = new EditorToolbarButton();
			created.name = BUTTON_NAME;
			created.text = string.Empty;
			created.clicked += OnClicked;
			created.style.alignSelf = Align.Center;
			created.style.marginLeft = 2f;
			ApplyIconSize(created);
			return created;
		}

		private static void ApplyIconSize (EditorToolbarButton target)
		{
			VisualElement iconElement = target.Q(className: "unity-editor-toolbar-element__icon");

			if (iconElement == null)
			{
				return;
			}

			iconElement.style.width = ICON_SIZE;
			iconElement.style.height = ICON_SIZE;
			iconElement.style.alignSelf = Align.Center;
		}

		private static void OnClicked ()
		{
			SimulatorSettingsWindow.Open();
		}

		private static void ApplyVisualState (ToolbarState state)
		{
			if (button == null)
			{
				return;
			}

			if (state == ToolbarState.Blocked)
			{
				button.icon = EditorGUIUtility.IconContent(BLOCKED_ICON).image as Texture2D;
				button.tooltip = "Simple Meta Simulator: ENABLED, but a real headset (Quest Link) is detected - the simulator will not start.";
			}
			else if (state == ToolbarState.Enabled)
			{
				button.icon = EditorGUIUtility.IconContent(ACTIVE_ICON).image as Texture2D;
				button.tooltip = "Simple Meta Simulator: ENABLED";
			}
			else
			{
				button.icon = EditorGUIUtility.IconContent(INACTIVE_ICON).image as Texture2D;
				button.tooltip = "Simple Meta Simulator: DISABLED";
			}

			ApplyIconSize(button);
		}

		private enum ToolbarState
		{
			Disabled = 0,
			Enabled = 1,
			Blocked = 2
		}

		private static EditorWindow FindToolbarWindow ()
		{
			UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(typeof(EditorWindow));

			for (int i = 0; i < windows.Length; i++)
			{
				if (windows[i].GetType().FullName == TOOLBAR_WINDOW_TYPE)
				{
					return windows[i] as EditorWindow;
				}
			}

			return null;
		}
	}
}
