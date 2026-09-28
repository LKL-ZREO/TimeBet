namespace TimeBet.Domain;

public sealed class DomainRuleException : InvalidOperationException
{
    public DomainRuleException(string message)
        : base(message)
    {
    }
}
