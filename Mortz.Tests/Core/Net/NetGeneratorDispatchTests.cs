using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Dispatch and codec behaviour no production message covers yet: a
/// nullable byte-enum field, and a row whose constructor rejects bad
/// arguments.</summary>
public class NetGeneratorDispatchTests
{
    private const int NO_TINT = -1;
    private const int RED = 1;
    private const int BLUE = 2;

    private const string SOURCE = """
        using System;
        using Mortz.Core.Net;

        namespace GenTest;

        public enum Tint : byte
        {
            RED = 1,
            BLUE = 2,
        }

        [NetRow]
        public readonly partial record struct Pair
        {
            public Pair(int first, int second)
            {
                if (first < 0)
                    throw new ArgumentException("first must not be negative", nameof(first));
                First = first;
                Second = second;
            }

            public int First { get; }

            public int Second { get; }
        }

        [NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
        public readonly partial record struct TintMsg(Tint? Colour, Pair[] Pairs);

        public static class Probe
        {
            public const int NO_TINT = -1;

            private static int _tint = NO_TINT;
            private static int _pairs = -1;
            private static bool _throwOnReceive;

            static Probe() => TintMsg.Received += OnReceived;

            public static void Reset()
            {
                _tint = NO_TINT;
                _pairs = -1;
                _throwOnReceive = false;
            }

            public static void ThrowOnReceive() => _throwOnReceive = true;

            public static int Tint() => _tint;

            public static int Pairs() => _pairs;

            public static bool Dispatch(byte[] payload) =>
                NetRegistry.Dispatch(NetRegistry.ID_TintMsg, 7, payload, false);

            public static byte[] Encode(int tint) =>
                TintMsg.Serialize(new TintMsg(ToTint(tint), []));

            private static Tint? ToTint(int tint)
            {
                if (tint == NO_TINT)
                    return null;
                return (Tint)tint;
            }

            private static void OnReceived(TintMsg message)
            {
                _tint = ToInt(message.Colour);
                _pairs = message.Pairs.Length;
                if (_throwOnReceive)
                    throw new ArgumentException("subscriber boom");
            }

            private static int ToInt(Tint? tint)
            {
                if (tint is Tint value)
                    return (int)value;
                return NO_TINT;
            }
        }
        """;

    private static readonly Lazy<NetGenResult> _generated = new(() => NetGenHarness.Run(SOURCE));

    private static Action Reset => Bind<Action>("Reset");

    private static Action ThrowOnReceive => Bind<Action>("ThrowOnReceive");

    private static Func<byte[], bool> Dispatch => Bind<Func<byte[], bool>>("Dispatch");

    private static Func<int, byte[]> Encode => Bind<Func<int, byte[]>>("Encode");

    private static Func<int> Tint => Bind<Func<int>>("Tint");

    private static Func<int> Pairs => Bind<Func<int>>("Pairs");

    [Theory]
    [InlineData(NO_TINT)]
    [InlineData(RED)]
    [InlineData(BLUE)]
    public void ANullableEnumRoundTrips(int tint)
    {
        Reset();
        Assert.True(Dispatch(Encode(tint)));
        Assert.Equal(tint, Tint());
    }

    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)200)]
    [InlineData(byte.MaxValue)]
    public void AnEnumValueThatDoesNotExistDecodesToNull(byte tint)
    {
        Reset();
        Assert.True(Dispatch(Bytes(w =>
        {
            w.Write(tint);
            w.Write(0);
        })));
        Assert.Equal(NO_TINT, Tint());
        Assert.Equal(0, Pairs());
    }

    [Fact]
    public void ARowConstructorRejectingItsArgumentsIsAMalformedMessage()
    {
        Reset();
        Assert.False(Dispatch(Bytes(w =>
        {
            w.Write((byte)RED);
            w.Write(1);
            w.Write(-1);
            w.Write(0);
        })));
        Assert.Equal(-1, Pairs());
    }

    [Fact]
    public void ASubscriberThrowingIsNotAMalformedMessage()
    {
        Reset();
        ThrowOnReceive();
        byte[] payload = Encode(RED);
        ArgumentException thrown = Assert.Throws<ArgumentException>(() => Dispatch(payload));
        Assert.Equal("subscriber boom", thrown.Message);
        Reset();
    }

    private static T Bind<T>(string method)
        where T : Delegate
    {
        NetGenResult generated = _generated.Value;
        if (generated.Assembly == null)
        {
            throw new InvalidOperationException(
                $"the probe source did not compile: {string.Join("\n", generated.Diagnostics)}");
        }
        Type probe = generated.Assembly.GetType("GenTest.Probe")
            ?? throw new InvalidOperationException("GenTest.Probe is missing");
        return (T)(probe.GetMethod(method)
            ?? throw new InvalidOperationException($"GenTest.Probe.{method} is missing"))
            .CreateDelegate(typeof(T));
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }
}
