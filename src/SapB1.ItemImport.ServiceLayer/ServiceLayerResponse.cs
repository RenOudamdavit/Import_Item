using System.Net;

namespace SapB1.ItemImport.ServiceLayer;

/// <summary>
/// A fully-read Service Layer response. Reading the body eagerly keeps retry and disposal logic in
/// one place; item payloads are small enough that streaming would buy nothing.
/// </summary>
public sealed record ServiceLayerResponse(HttpStatusCode StatusCode, string Body)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
}
