namespace Mortz.Tests.Core.Net;

/// <summary>One message a router delivered, with the sender it carried.</summary>
public readonly record struct Delivery<TMsg>(int Sender, TMsg Message) where TMsg : struct;
