using Mortz.Core.Text;

namespace Mortz.Core.Chat;

public abstract record ChatLine
{
    public ChatLine()
    {
    }

    public abstract string Text { get; }

    public abstract RichText Render();

    public abstract record Remote : ChatLine
    {
        public Remote()
        {
        }
    }

    public sealed record Player : Remote
    {
        public Player(int senderId, string senderName, string text)
        {
            SenderId = RequirePeerId(senderId);
            SenderName = senderName;
            Text = RequireText(text);
        }

        public int SenderId { get; }
        public string SenderName { get; }
        public override string Text { get; }

        public override RichText Render() => ChatMarkdown.Render(Text);
    }

    public sealed record System : Remote
    {
        private readonly string _bbCode;

        public System(string text)
        {
            string valid = RequireText(text);
            _bbCode = new RichText(valid).ToString();
        }

        public System(RichText text)
        {
            ArgumentNullException.ThrowIfNull(text);
            _bbCode = RequireText(text.ToString());
        }

        public override string Text => _bbCode;

        public override RichText Render() => RichText.FromTrustedBbCode(_bbCode);

        public static System FromTrustedBbCode(string bbCode) =>
            new(RichText.FromTrustedBbCode(RequireText(bbCode)));
    }

    public sealed record Roll : Remote
    {
        public Roll(int senderId, string senderName, int value)
        {
            if (value is < DiceRoll.MIN or > DiceRoll.MAX)
                throw new ArgumentOutOfRangeException(nameof(value));
            SenderId = RequirePeerId(senderId);
            SenderName = senderName;
            Value = value;
        }

        public int SenderId { get; }
        public string SenderName { get; }
        public int Value { get; }
        public override string Text => Value.ToString();

        public override RichText Render() => new(Text);
    }

    public sealed record Private : ChatLine
    {
        public Private(string text)
        {
            string valid = RequireText(text);
            Text = new RichText(valid).ToString();
        }

        public Private(RichText text)
        {
            ArgumentNullException.ThrowIfNull(text);
            Text = RequireText(text.ToString());
        }

        public override string Text { get; }

        public override RichText Render() => RichText.FromTrustedBbCode(Text);
    }

    private static string RequireText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Chat line text cannot be empty.", nameof(text));
        return text;
    }

    private static int RequirePeerId(int peerId)
    {
        if (peerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(peerId));
        return peerId;
    }
}
