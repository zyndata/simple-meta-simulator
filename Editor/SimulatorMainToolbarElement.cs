#if SMS_MAIN_TOOLBAR_API
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;

namespace SMS.Editor
{
	/// <summary>
	/// Registers the simulator status button through the supported main toolbar API added in Unity 6.3.
	/// Unity 6.3 detects elements injected straight into the toolbar visual tree, moves them into its
	/// "Unsupported User Elements" group (docked Left, which is why the icon showed up on the far left)
	/// and logs a warning, so on 6.3+ the legacy injection in SimulatorToolbarButton is compiled out and
	/// this element is used instead. Docking Middle at index 1 puts the light immediately right of the
	/// play controls, which register as Middle index 0.
	/// </summary>
	internal static class SimulatorMainToolbarElement
	{
		private const string ELEMENT_PATH = "SMS/Simulator";
		private const string SHOWN_PREFS_KEY = "dev.gorny.sms.toolbarElementShown";
		private const string MAIN_TOOLBAR_TYPE = "UnityEditor.Toolbars.MainToolbar";

		private const int REVEAL_MAX_ATTEMPTS = 40;

		private static MainToolbarButton element;
		private static double nextRefreshTime;
		private static ToolbarState lastKnownState;
		private static bool hasCachedState;

		private static int revealAttempts;
		private static double nextRevealTime;

		/// <summary>
		/// A newly registered main toolbar element is docked but hidden (its overlay starts with
		/// displayed = false), so a fresh install would show nothing until the user enabled it from the
		/// toolbar context menu. This shows it exactly once per machine; afterwards the visibility the user
		/// picked is left alone. Retried on the update tick rather than through delayCall because the main
		/// toolbar is not necessarily built yet when load-time callbacks run, and a reveal attempted before
		/// it exists is silently dropped. Unity exposes only Refresh publicly, so the reveal goes through the
		/// internal MainToolbar.ShowAll and gives up quietly if that internal API ever changes.
		/// </summary>
		[InitializeOnLoadMethod]
		private static void ShowOnFirstInstall ()
		{
			if (EditorPrefs.GetBool(SHOWN_PREFS_KEY, false) == true)
			{
				return;
			}

			EditorApplication.update -= RevealTick;
			EditorApplication.update += RevealTick;
		}

		private static void RevealTick ()
		{
			if (EditorApplication.timeSinceStartup < nextRevealTime)
			{
				return;
			}

			nextRevealTime = EditorApplication.timeSinceStartup + SimulatorToolbarStatus.REFRESH_INTERVAL;
			revealAttempts++;

			if (TryReveal() == true || revealAttempts >= REVEAL_MAX_ATTEMPTS)
			{
				EditorApplication.update -= RevealTick;
				EditorPrefs.SetBool(SHOWN_PREFS_KEY, true);
			}
		}

		private static bool TryReveal ()
		{
			Type mainToolbarType = ResolveMainToolbarType();

			if (mainToolbarType == null)
			{
				return false;
			}

			MethodInfo showAll = mainToolbarType.GetMethod("ShowAll", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(string) }, null);

			if (showAll == null)
			{
				return true;
			}

			showAll.Invoke(null, new object[] { ELEMENT_PATH });
			MainToolbar.Refresh(ELEMENT_PATH);

			return IsOverlayDisplayed(mainToolbarType);
		}

		/// <summary>
		/// Confirms the reveal actually took, so the attempt is not marked done while the toolbar is still
		/// being built. Returns true when the state cannot be read at all, so an internal API change ends the
		/// retry loop instead of running it to exhaustion every session.
		/// </summary>
		private static bool IsOverlayDisplayed (Type mainToolbarType)
		{
			MethodInfo tryGetOverlay = mainToolbarType.GetMethod("TryGetOverlay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

			if (tryGetOverlay == null)
			{
				return true;
			}

			object[] args = new object[] { ELEMENT_PATH, null };

			if ((bool)tryGetOverlay.Invoke(null, args) == false || args[1] == null)
			{
				return false;
			}

			PropertyInfo displayed = args[1].GetType().GetProperty("displayed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

			if (displayed == null)
			{
				return true;
			}

			return (bool)displayed.GetValue(args[1]);
		}

		private static Type ResolveMainToolbarType ()
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

			for (int i = 0; i < assemblies.Length; i++)
			{
				Type found = assemblies[i].GetType(MAIN_TOOLBAR_TYPE, false);

				if (found != null)
				{
					return found;
				}
			}

			return null;
		}

		[MainToolbarElement(ELEMENT_PATH, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 1)]
		private static MainToolbarElement Create ()
		{
			ToolbarState state = SimulatorToolbarStatus.Resolve();

			element = new MainToolbarButton(BuildContent(state), OnClicked);
			lastKnownState = state;
			hasCachedState = true;

			EditorApplication.update -= OnEditorUpdate;
			EditorApplication.update += OnEditorUpdate;

			return element;
		}

		private static void OnEditorUpdate ()
		{
			if (element == null)
			{
				return;
			}

			if (EditorApplication.timeSinceStartup < nextRefreshTime)
			{
				return;
			}

			nextRefreshTime = EditorApplication.timeSinceStartup + SimulatorToolbarStatus.REFRESH_INTERVAL;

			ToolbarState state = SimulatorToolbarStatus.Resolve();

			if (hasCachedState == true && state == lastKnownState)
			{
				return;
			}

			lastKnownState = state;
			hasCachedState = true;
			element.content = BuildContent(state);

			// Assigning content does not repaint the docked overlay, so the light kept its first icon for the
			// whole session. Refresh rebuilds the element through Create(), which picks the current state up
			// again - Create() also refreshes the cached state, so this settles instead of looping.
			MainToolbar.Refresh(ELEMENT_PATH);
		}

		private static MainToolbarContent BuildContent (ToolbarState state)
		{
			return new MainToolbarContent(SimulatorToolbarStatus.GetIcon(state), SimulatorToolbarStatus.GetTooltip(state));
		}

		private static void OnClicked ()
		{
			SimulatorSettingsWindow.Open();
		}
	}
}
#endif
