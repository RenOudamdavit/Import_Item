using SapB1.ItemImport.Core.Logging;
using SapB1.ItemImport.ServiceLayer;

namespace SapB1.ItemImport.Cli;

/// <summary>
/// Logs in to the SAP Service Layer, reports what it connected to, and logs out.
/// </summary>
/// <remarks>
/// Separating "can I reach SAP at all?" from "is my file correct?" is the difference between a
/// one-minute fix and an afternoon of guessing. It writes nothing to SAP beyond a login and logout.
/// </remarks>
public static class ConnectionTester
{
    public static async Task<int> RunAsync(
        ServiceLayerOptions options,
        IImportLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        await using var session = ServiceLayerSession.Create(options, logger);
        await session.LoginAsync(cancellationToken).ConfigureAwait(false);

        Console.Out.WriteLine();
        Console.Out.WriteLine("Connection OK — SAP accepted the login.");
        Console.Out.WriteLine($"  Service Layer : {session.BaseUri}");
        Console.Out.WriteLine($"  Company       : {options.CompanyDb}");
        Console.Out.WriteLine($"  User          : {options.UserName}");
        Console.Out.WriteLine($"  SAP version   : {session.Version ?? "(not reported by the server)"}");
        Console.Out.WriteLine($"  TLS           : {DescribeTls(options)}");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Next: run an import in dry-run mode, e.g.");
        Console.Out.WriteLine("  sapb1-import-items --file items.csv --dry-run");

        return ExitCodes.Success;
    }

    private static string DescribeTls(ServiceLayerOptions options)
    {
        if (options.TrustAnyServerCertificate)
        {
            return "validation DISABLED (insecure — pin the certificate instead)";
        }

        var thumbprint = options.NormalizedThumbprint();

        return thumbprint is null
            ? "validated against the machine trust store"
            : $"pinned to thumbprint {thumbprint}";
    }
}
