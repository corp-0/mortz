using Godot;
using Mortz.Client.Chat;
using Mortz.Client.Match;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Client;

/// <summary>Actions are held for real, otherwise nothing is pressed in a test
/// process and the suppression assertion passes on its own.</summary>
[Collection(nameof(MortzGodotCollection))]
public class InputSamplerTests : IDisposable
{
    private readonly object _owner = new();

    public void Dispose()
    {
        Input.ActionRelease("move_left");
        Input.ActionRelease("fire");
        ChatInputGuard.SetTyping(_owner, false);
    }

    [Fact]
    public void HeldActionsBecomeTheirSimButtons()
    {
        Input.ActionPress("move_left");
        Input.ActionPress("fire");

        Assert.Equal(InputButtons.LEFT | InputButtons.FIRE, InputSampler.Sample());
    }

    [Fact]
    public void TypingSuppressesEveryGameplayButton()
    {
        Input.ActionPress("move_left");
        Input.ActionPress("fire");
        Assert.NotEqual(InputButtons.NONE, InputSampler.Sample());

        ChatInputGuard.SetTyping(_owner, true);

        Assert.Equal(InputButtons.NONE, InputSampler.Sample());
    }
}
