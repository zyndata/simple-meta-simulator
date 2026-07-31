#if !SMS_MAIN_TOOLBAR_API
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace SMS.Editor
{
	/// <summary>
	/// Injects a status toggle into the main toolbar, to the right of the Play button, showing whether the
	/// simulator is enabled. Clicking opens the simulator settings window. Injects directly into the live
	/// toolbar visual tree because editor versions before 6.3 expose no supported extension point.
	/// Compiled out on Unity 6.3+, where SimulatorMainToolbarElement uses the supported API instead -
	/// 6.3 quarantines injected elements into its "Unsupported User Elements" group and logs a warning.
	/// </summary>
	[InitializeOnLoad]
	internal static class SimulatorToolbarButton
	{
		private const string BUTTON_NAME = "SMSSimulatorToolbarButton";
		private const string PLAY_MODE_ELEMENT_NAME = "PlayMode";
		private const string PLAY_MODE_ELEMENT_TYPE = "PlayModeButtons";
		private const string PLAY_MODE_ZONE_NAME = "ToolbarZonePlayMode";
		private const string RIGHT_ZONE_NAME = "ToolbarZoneRightAlign";
		private const string ZONE_CLASS = "unity-editor-toolbar-container__zone";
		private const string MIDDLE_CONTAINER_CLASS = "unity-overlay-container__middle-container";
		private const string META_OVERLAY_NAME = "MetaXR/PlayCompanion";
		private const string TOOLBAR_TYPE = "UnityEditor.Toolbar";
		private const string TOOLBAR_WINDOW_TYPE = "UnityEditor.MainToolbarWindow";

		private const float ICON_SIZE = 16f;

		private static EditorToolbarButton button;
		private static double nextRefreshTime;
		private static ToolbarState lastKnownState;
		private static bool hasCachedState;

		private static Type toolbarType;
		private static FieldInfo toolbarInstanceField;
		private static FieldInfo toolbarRootField;
		private static bool toolbarTypeResolved;

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

			nextRefreshTime = EditorApplication.timeSinceStartup + SimulatorToolbarStatus.REFRESH_INTERVAL;

			EnsureInjected();

			if (button == null)
			{
				return;
			}

			ToolbarState state = SimulatorToolbarStatus.Resolve();

			if (hasCachedState == true && state == lastKnownState)
			{
				return;
			}

			lastKnownState = state;
			hasCachedState = true;
			ApplyVisualState(state);
		}

		private static void EnsureInjected ()
		{
			VisualElement root = FindToolbarRoot();

			if (root == null)
			{
				return;
			}

			VisualElement anchor = FindPlayModeElement(root);

			if (button != null && button.panel != null && IsPlacedAfterAnchor(anchor) == true)
			{
				return;
			}

			// Unity 6.3 repopulates the toolbar zones after the button is injected, which leaves the button
			// parented to a discarded zone (it keeps a panel, so a plain "already injected" check never
			// recovers) while the live zone gets a fresh play element. Placement is therefore re-validated
			// against the current play element every tick and repaired when it no longer matches.
			VisualElement stale = root.Q<VisualElement>(BUTTON_NAME);

			if (stale != null && ReferenceEquals(stale, button) == false)
			{
				stale.RemoveFromHierarchy();
			}

			if (button == null)
			{
				button = CreateButton();
			}
			else
			{
				button.RemoveFromHierarchy();
			}

			if (Place(root, anchor) == false)
			{
				return;
			}

			hasCachedState = false;
			ApplyVisualState(SimulatorToolbarStatus.Resolve());
		}

		/// <summary>
		/// True when the button is still the immediate right-hand sibling of the play element. When no play
		/// element is found there is nothing to validate against, so any current placement is accepted rather
		/// than churning the button between containers every tick.
		/// </summary>
		private static bool IsPlacedAfterAnchor (VisualElement anchor)
		{
			if (button == null || button.parent == null)
			{
				return false;
			}

			if (anchor == null || anchor.parent == null)
			{
				return true;
			}

			if (ReferenceEquals(button.parent, anchor.parent) == false)
			{
				return false;
			}

			return button.parent.IndexOf(button) == button.parent.IndexOf(anchor) + 1;
		}

		/// <summary>
		/// Places the button immediately to the right of the play controls by anchoring to the play element
		/// itself rather than to a named zone. Zone contents differ between editor versions (Unity 6.2 lays the
		/// zones out directly, Unity 6.3 wraps them in ToolbarZone elements it repopulates), so a zone name is
		/// not a reliable position; the play element is present in both.
		/// </summary>
		private static bool Place (VisualElement root, VisualElement anchor)
		{
			if (anchor != null && anchor.parent != null)
			{
				VisualElement parent = anchor.parent;
				int anchorIndex = parent.IndexOf(anchor);

				if (anchorIndex >= 0)
				{
					parent.Insert(anchorIndex + 1, button);
					return true;
				}
			}

			VisualElement container = FindContainer(root);

			if (container == null)
			{
				return false;
			}

			VisualElement metaOverlay = root.Q(META_OVERLAY_NAME);

			if (metaOverlay != null && metaOverlay.parent == container)
			{
				int metaIndex = container.IndexOf(metaOverlay);
				container.Insert(metaIndex + 1, button);
			}
			else
			{
				container.Add(button);
			}

			return true;
		}

		private static VisualElement FindPlayModeElement (VisualElement element)
		{
			if (element.name == PLAY_MODE_ELEMENT_NAME || element.GetType().Name == PLAY_MODE_ELEMENT_TYPE)
			{
				return element;
			}

			for (int i = 0; i < element.childCount; i++)
			{
				VisualElement found = FindPlayModeElement(element[i]);

				if (found != null)
				{
					return found;
				}
			}

			return null;
		}

		/// <summary>
		/// Fallback container when the play element cannot be found, newest Unity layout first. Unity 6 splits
		/// the main toolbar into named zones; older layouts used a single overlay middle container.
		/// </summary>
		private static VisualElement FindContainer (VisualElement root)
		{
			VisualElement playModeZone = root.Q(PLAY_MODE_ZONE_NAME);

			if (playModeZone != null)
			{
				return playModeZone;
			}

			VisualElement rightZone = root.Q(RIGHT_ZONE_NAME);

			if (rightZone != null)
			{
				return rightZone;
			}

			VisualElement middleContainer = root.Q(className: MIDDLE_CONTAINER_CLASS);

			if (middleContainer != null)
			{
				return middleContainer;
			}

			return root.Q(className: ZONE_CLASS);
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

			button.icon = SimulatorToolbarStatus.GetIcon(state);
			button.tooltip = SimulatorToolbarStatus.GetTooltip(state);

			ApplyIconSize(button);
		}

		/// <summary>
		/// Resolves the main toolbar's visual tree root. The main toolbar is NOT an EditorWindow - it is the
		/// internal <c>UnityEditor.Toolbar</c> (a GUIView), reached through its static <c>get</c> instance and
		/// its <c>m_Root</c> field. The EditorWindow lookup is kept only as a fallback for editor versions
		/// that host the toolbar in a window.
		/// </summary>
		private static VisualElement FindToolbarRoot ()
		{
			if (toolbarTypeResolved == false)
			{
				toolbarTypeResolved = true;
				toolbarType = ResolveToolbarType();

				if (toolbarType != null)
				{
					toolbarInstanceField = toolbarType.GetField("get", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
					toolbarRootField = toolbarType.GetField("m_Root", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				}
			}

			if (toolbarInstanceField != null && toolbarRootField != null)
			{
				object instance = toolbarInstanceField.GetValue(null);

				if (instance != null)
				{
					VisualElement root = toolbarRootField.GetValue(instance) as VisualElement;

					if (root != null)
					{
						return root;
					}
				}
			}

			EditorWindow toolbarWindow = FindToolbarWindow();

			if (toolbarWindow == null)
			{
				return null;
			}

			return toolbarWindow.rootVisualElement;
		}

		private static Type ResolveToolbarType ()
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

			for (int i = 0; i < assemblies.Length; i++)
			{
				Type found = assemblies[i].GetType(TOOLBAR_TYPE, false);

				if (found != null)
				{
					return found;
				}
			}

			return null;
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
#endif
