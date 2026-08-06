namespace Mortz.Server.Match.Scoring;

/// <summary>A suicide's kill handed to an enemy; Kills is their tally after.</summary>
public readonly record struct KillReward(int PeerId, int Kills);
