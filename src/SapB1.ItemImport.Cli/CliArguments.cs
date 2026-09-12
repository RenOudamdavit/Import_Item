using System.Globalization;

namespace SapB1.ItemImport.Cli;

/// <summary>Parsed command line. Every value is optional so configuration can supply the rest.</summary>
public sealed class CliArguments
{
    public string? File { get; private set; }

    public string? ConfigPath { get; private set; }

    public string? MappingPath { get; private set; }

    public string? ReportPath { get; private set; }

    public bool FailuresOnly { get; private set; }

    public bool DryRun { get; private set; }

    public string? Mode { get; private set; }

    public string? Delimiter { get; private set; }

    public string? Encoding { get; private set; }

    public string? Culture { get; private set; }

    public int? MaxErrors { get; private set; }

    public int? BatchSize { get; private set; }

    public string? UnknownColumns { get; private set; }

    public bool AllowDuplicates { get; private set; }

    public string? NullToken { get; private set; }

    public string? ItemCodePattern { get; private set; }

    public int? DefaultPriceList { get; private set; }

    public bool Verbose { get; private set; }

    public string? Url { get; private set; }

    public string? Company { get; private set; }

    public string? User { get; private set; }

    public bool TrustAnyCertificate { get; private set; }

    public string? CertificateThumbprint { get; private set; }

    public bool ShowHelp { get; private set; }

    public bool ShowFields { get; private set; }

    public static CliArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var parsed = new CliArguments();

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];

            switch (argument)
            {
                case "-h":
                case "--help":
                    parsed.ShowHelp = true;
                    break;
                case "--show-fields":
                    parsed.ShowFields = true;
                    break;
                case "-f":
                case "--file":
                    parsed.File = ReadValue(args, ref i, argument);
                    break;
                case "-c":
                case "--config":
                    parsed.ConfigPath = ReadValue(args, ref i, argument);
                    break;
                case "--mapping":
                    parsed.MappingPath = ReadValue(args, ref i, argument);
                    break;
                case "--report":
                    parsed.ReportPath = ReadValue(args, ref i, argument);
                    break;
                case "--failures-only":
                    parsed.FailuresOnly = true;
                    break;
                case "--dry-run":
                    parsed.DryRun = true;
                    break;
                case "--mode":
                    parsed.Mode = ReadValue(args, ref i, argument);
                    break;
                case "--delimiter":
                    parsed.Delimiter = ReadValue(args, ref i, argument);
                    break;
                case "--encoding":
                    parsed.Encoding = ReadValue(args, ref i, argument);
                    break;
                case "--culture":
                    parsed.Culture = ReadValue(args, ref i, argument);
                    break;
                case "--max-errors":
                    parsed.MaxErrors = ReadInt(args, ref i, argument);
                    break;
                case "--batch-size":
                    parsed.BatchSize = ReadInt(args, ref i, argument);
                    break;
                case "--unknown-columns":
                    parsed.UnknownColumns = ReadValue(args, ref i, argument);
                    break;
                case "--allow-duplicates":
                    parsed.AllowDuplicates = true;
                    break;
                case "--null-token":
                    parsed.NullToken = ReadValue(args, ref i, argument);
                    break;
                case "--item-code-pattern":
                    parsed.ItemCodePattern = ReadValue(args, ref i, argument);
                    break;
                case "--default-price-list":
                    parsed.DefaultPriceList = ReadInt(args, ref i, argument);
                    break;
                case "-v":
                case "--verbose":
                    parsed.Verbose = true;
                    break;
                case "--url":
                    parsed.Url = ReadValue(args, ref i, argument);
                    break;
                case "--company":
                    parsed.Company = ReadValue(args, ref i, argument);
                    break;
                case "--user":
                    parsed.User = ReadValue(args, ref i, argument);
                    break;
                case "--trust-any-certificate":
                    parsed.TrustAnyCertificate = true;
                    break;
                case "--certificate-thumbprint":
                    parsed.CertificateThumbprint = ReadValue(args, ref i, argument);
                    break;
                case "--password":
                    throw new UsageException(
                        "--password is not supported: a password on the command line is visible to every user via the "
                        + "process list and is written to shell history. Set the SAPB1_PASSWORD environment variable "
                        + "or put it in the configuration file instead.");
                default:
                    throw new UsageException($"Unrecognised argument '{argument}'. Run with --help to see the options.");
            }
        }

        return parsed;
    }

    private static string ReadValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new UsageException($"{name} requires a value.");
        }

        index++;
        return args[index];
    }

    private static int ReadInt(string[] args, ref int index, string name)
    {
        var raw = ReadValue(args, ref index, name);

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new UsageException($"{name} expects a whole number but got '{raw}'.");
        }

        return value;
    }
}
