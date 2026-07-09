namespace SMS
{
	/// <summary>
	/// Determines how movement input is routed. Both: WSAD/QE moves the whole rig (camera + both
	/// hands together) and the hands stay in front of the head while looking around. CycleKey: press
	/// a key to cycle which element WSAD/QE moves (head / left hand / right hand / both).
	/// </summary>
	public enum HandActivationMode
	{
		Both = 0,
		CycleKey = 1
	}
}
