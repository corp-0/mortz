using Mortz.Client;
using Mortz.Client.Chat;
using Mortz.Core;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Client;

public class InputSamplerTests
{
    [Fact]
    public void TypingSuppressesEveryGameplayButton()
    {
        object owner = new();
        ChatInputGuard.SetTyping(owner, true);
        try
        {
            Assert.Equal(InputButtons.None, InputSampler.Sample());
        }
        finally
        {
            ChatInputGuard.SetTyping(owner, false);
        }
    }
}
