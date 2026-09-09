namespace Mortz.Core.Identity;

public enum AccountProvider
{
    STEAM,
}

public readonly record struct VerifiedAccount(AccountProvider Provider, ulong AccountId);
