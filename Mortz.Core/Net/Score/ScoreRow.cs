namespace Mortz.Core.Net.Score;

[NetRow]
public readonly partial record struct ScoreRow(int PeerId, int Kills, int Deaths);
