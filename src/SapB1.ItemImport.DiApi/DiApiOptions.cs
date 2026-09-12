namespace SapB1.ItemImport.DiApi;

/// <summary>Connection settings for the SAP Business One DI API.</summary>
public sealed class DiApiOptions
{
    /// <summary>Database server, e.g. <c>SQLHOST\SQLEXPRESS</c> or <c>hana-host:30015</c>.</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Company database, e.g. <c>SBODEMOGB</c>.</summary>
    public string CompanyDb { get; set; } = string.Empty;

    /// <summary>License server, e.g. <c>sap-host:30000</c>.</summary>
    public string LicenseServer { get; set; } = string.Empty;

    /// <summary>
    /// SAP <c>BoDataServerTypes</c> member name, e.g. <c>dst_MSSQL2019</c> or <c>dst_HANADB</c>.
    /// It must match the actual database or <c>Connect</c> fails with a misleading error.
    /// </summary>
    public string DbServerType { get; set; } = "dst_MSSQL2019";

    /// <summary>Database user (not needed when <see cref="UseTrusted"/> is set).</summary>
    public string DbUserName { get; set; } = string.Empty;

    /// <summary>Database password. Supply it from the environment, never from an argument.</summary>
    public string DbPassword { get; set; } = string.Empty;

    /// <summary>SAP Business One application user.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>SAP Business One application password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Use Windows authentication for the database connection.</summary>
    public bool UseTrusted { get; set; }

    /// <summary>Optional SAP <c>BoSuppLangs</c> member name, e.g. <c>ln_English</c>.</summary>
    public string? Language { get; set; }

    public void Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Server))
        {
            problems.Add("Server is required.");
        }

        if (string.IsNullOrWhiteSpace(CompanyDb))
        {
            problems.Add("CompanyDb is required.");
        }

        if (string.IsNullOrWhiteSpace(LicenseServer))
        {
            problems.Add("LicenseServer is required (host:30000).");
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            problems.Add("UserName is required.");
        }

        if (string.IsNullOrEmpty(Password))
        {
            problems.Add("Password is required.");
        }

        if (!UseTrusted && string.IsNullOrWhiteSpace(DbUserName))
        {
            problems.Add("DbUserName is required unless UseTrusted is set.");
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                "The SAP DI API configuration is incomplete:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
        }
    }
}
