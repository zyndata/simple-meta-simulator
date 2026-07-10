using UnityEngine;
using UnityEngine.InputSystem;

namespace SMS
{
	/// <summary>
	/// Loads and exposes the simulator input bindings. An InputActionAsset can be supplied by the
	/// editor layer (loaded from the package by path, not from Resources, so nothing ships in a
	/// build). If none is supplied, a code-defined fallback set of default bindings is built so the
	/// simulator works out of the box.
	/// </summary>
	public class SimulatorInputActions
	{
		private const string MAP_NAME = "Simulator";

		private InputActionAsset asset;

		private InputAction moveAction;
		private InputAction verticalAction;
		private InputAction lookDeltaAction;
		private InputAction lookModifierAction;
		private InputAction cycleAction;
		private InputAction leftGrabAction;
		private InputAction leftGripAction;
		private InputAction rightGrabAction;
		private InputAction rightGripAction;
		private InputAction leftPrimaryAction;
		private InputAction leftSecondaryAction;
		private InputAction rightPrimaryAction;
		private InputAction rightSecondaryAction;

		public InputActionAsset Asset => asset;

		public SimulatorInputActions (InputActionAsset providedAsset)
		{
			asset = providedAsset;

			if (asset == null)
			{
				asset = BuildFallbackAsset();
			}

			ResolveActions();
		}

		public void Enable ()
		{
			asset.Enable();
		}

		public void Disable ()
		{
			asset.Disable();
		}

		public Vector2 Move ()
		{
			return moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
		}

		public float Vertical ()
		{
			return verticalAction != null ? verticalAction.ReadValue<float>() : 0f;
		}

		public Vector2 LookDelta ()
		{
			return lookDeltaAction != null ? lookDeltaAction.ReadValue<Vector2>() : Vector2.zero;
		}

		public bool LookModifier ()
		{
			return lookModifierAction != null && lookModifierAction.IsPressed();
		}

		public bool CyclePressed ()
		{
			return cycleAction != null && cycleAction.WasPressedThisFrame();
		}

		public bool LeftGrab ()
		{
			return leftGrabAction != null && leftGrabAction.IsPressed();
		}

		public bool LeftGrip ()
		{
			return leftGripAction != null && leftGripAction.IsPressed();
		}

		public bool RightGrab ()
		{
			return rightGrabAction != null && rightGrabAction.IsPressed();
		}

		public bool RightGrip ()
		{
			return rightGripAction != null && rightGripAction.IsPressed();
		}

		public bool LeftPrimary ()
		{
			return leftPrimaryAction != null && leftPrimaryAction.IsPressed();
		}

		public bool LeftSecondary ()
		{
			return leftSecondaryAction != null && leftSecondaryAction.IsPressed();
		}

		public bool RightPrimary ()
		{
			return rightPrimaryAction != null && rightPrimaryAction.IsPressed();
		}

		public bool RightSecondary ()
		{
			return rightSecondaryAction != null && rightSecondaryAction.IsPressed();
		}

		private void ResolveActions ()
		{
			InputActionMap map = asset.FindActionMap(MAP_NAME, false);

			if (map == null)
			{
				return;
			}

			moveAction = map.FindAction("Move", false);
			verticalAction = map.FindAction("Vertical", false);
			lookDeltaAction = map.FindAction("LookDelta", false);
			lookModifierAction = map.FindAction("LookModifier", false);
			cycleAction = map.FindAction("Cycle", false);
			leftGrabAction = map.FindAction("LeftGrab", false);
			leftGripAction = map.FindAction("LeftGrip", false);
			rightGrabAction = map.FindAction("RightGrab", false);
			rightGripAction = map.FindAction("RightGrip", false);
			leftPrimaryAction = map.FindAction("LeftPrimary", false);
			leftSecondaryAction = map.FindAction("LeftSecondary", false);
			rightPrimaryAction = map.FindAction("RightPrimary", false);
			rightSecondaryAction = map.FindAction("RightSecondary", false);
		}

		private InputActionAsset BuildFallbackAsset ()
		{
			InputActionAsset built = ScriptableObject.CreateInstance<InputActionAsset>();
			InputActionMap map = built.AddActionMap(MAP_NAME);

			InputAction move = map.AddAction("Move", InputActionType.Value);
			move.AddCompositeBinding("2DVector")
				.With("Up", "<Keyboard>/w")
				.With("Down", "<Keyboard>/s")
				.With("Left", "<Keyboard>/a")
				.With("Right", "<Keyboard>/d");

			InputAction vertical = map.AddAction("Vertical", InputActionType.Value);
			vertical.AddCompositeBinding("1DAxis")
				.With("Negative", "<Keyboard>/q")
				.With("Positive", "<Keyboard>/e");

			map.AddAction("LookDelta", InputActionType.Value, "<Mouse>/delta");
			map.AddAction("LookModifier", InputActionType.Button, "<Mouse>/rightButton");

			map.AddAction("Cycle", InputActionType.Button, "<Keyboard>/tab");

			map.AddAction("RightGrab", InputActionType.Button, "<Keyboard>/g");
			map.AddAction("RightGrip", InputActionType.Button, "<Keyboard>/f");
			map.AddAction("LeftGrab", InputActionType.Button, "<Keyboard>/v");
			map.AddAction("LeftGrip", InputActionType.Button, "<Keyboard>/c");

			map.AddAction("RightPrimary", InputActionType.Button, "<Keyboard>/h");
			map.AddAction("RightSecondary", InputActionType.Button, "<Keyboard>/j");
			map.AddAction("LeftPrimary", InputActionType.Button, "<Keyboard>/b");
			map.AddAction("LeftSecondary", InputActionType.Button, "<Keyboard>/n");

			return built;
		}
	}
}
