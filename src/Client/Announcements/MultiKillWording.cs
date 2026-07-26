namespace Mortz.Client.Announcements;

/// <summary>Reads "{name} {Link} {Loud}!". Heat ranks the tier from 0.</summary>
public readonly record struct MultiKillWording(string Link, string Loud, int Heat);
