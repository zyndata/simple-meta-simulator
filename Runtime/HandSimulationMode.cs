namespace SMS
{
	/// <summary>
	/// Selects which Meta Interaction SDK (ISDK) input branch the simulator drives on a rig that
	/// carries both controller and hand interactors. Off: only controllers are simulated, so the
	/// rig runs its "Controller and No Hand" branch. WithControllers: controllers plus simulated
	/// hand tracking, the way a real headset reports controllers held in tracked hands, so the
	/// "Controller and Hand" branch runs. HandsOnly: the controllers are reported as disconnected
	/// and only hands are simulated, so the "Hand and No Controller" branch runs (hand ray, hand
	/// poke, microgesture locomotion).
	/// </summary>
	public enum HandSimulationMode
	{
		Off = 0,
		WithControllers = 1,
		HandsOnly = 2
	}
}
