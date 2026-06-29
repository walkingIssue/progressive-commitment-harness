namespace Pch.Providers.ModelActions;

public sealed class ModelActionRunnerException : Exception
{
    public ModelActionRunnerException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
