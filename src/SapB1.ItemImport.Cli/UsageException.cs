namespace SapB1.ItemImport.Cli;

/// <summary>The command line could not be understood. Carries a message meant for the operator.</summary>
public sealed class UsageException : Exception
{
    public UsageException(string message)
        : base(message)
    {
    }
}
