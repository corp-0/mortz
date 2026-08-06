namespace Mortz.Tests.Server;

/// <summary>One thing the server put on the wire. Target 0 means broadcast.</summary>
internal readonly record struct Sent(int Target, object Message);
