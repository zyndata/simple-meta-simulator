namespace SMS
{
	/// <summary>
	/// Determines how the simulated face buttons (X/Y on the left hand, A/B on the right) respond
	/// to their keyboard keys. Held: the button is pressed only while the key is held down.
	/// Toggle: each key press flips the button's pressed state until pressed again.
	/// </summary>
	public enum ButtonInputMode
	{
		Held = 0,
		Toggle = 1
	}
}
