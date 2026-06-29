namespace Pch.Providers.Errors;

public abstract class ProviderException : Exception
{
    protected ProviderException(string provider, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
    }

    public string Provider { get; }
}

public sealed class ProviderUnavailableException : ProviderException
{
    public ProviderUnavailableException(string provider, string message, int? statusCode = null, Exception? innerException = null)
        : base(provider, message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}

public sealed class ProviderCreditExhaustedException : ProviderException
{
    public ProviderCreditExhaustedException(string provider, string message, decimal? remainingCredits = null)
        : base(provider, message)
    {
        RemainingCredits = remainingCredits;
    }

    public decimal? RemainingCredits { get; }
}

public sealed class ProviderEmptyResponseException : ProviderException
{
    public ProviderEmptyResponseException(string provider, string message)
        : base(provider, message)
    {
    }
}

public sealed class ProviderMalformedResponseException : ProviderException
{
    public ProviderMalformedResponseException(string provider, string message, Exception? innerException = null)
        : base(provider, message, innerException)
    {
    }
}
