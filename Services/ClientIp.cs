namespace CardTradeBuddy.Services;

/// <summary>
/// The visitor's address, used for rate limiting. Behind Cloudflare the connection comes from Cloudflare, so the real address is in
/// the CF-Connecting-IP header: that header is only trusted when <c>Network:BehindCloudflare</c> is true, because anyone reaching the
/// server directly could send it. Not a security boundary on its own: the LoginServer also locks an account after repeated failures.
/// </summary>
public static class ClientIp
{
    public static string? Get(HttpContext context)
    {
        var behindCloudflare = context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("Network:BehindCloudflare");
        return behindCloudflare && context.Request.Headers["CF-Connecting-IP"].FirstOrDefault() is { Length: > 0 } header
            ? header
            : context.Connection.RemoteIpAddress?.ToString();
    }
}
