namespace Mortz.E2E.Tests.Harness;

public readonly record struct ProcessLogLine(DateTimeOffset At, string Stream, string Text);
