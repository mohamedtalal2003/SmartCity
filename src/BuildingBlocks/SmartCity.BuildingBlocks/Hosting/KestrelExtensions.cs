using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SmartCity.BuildingBlocks;

public static class KestrelExtensions
{
    /// <summary>
    /// Two plain-text endpoints: <c>Ports:Http</c> as HTTP/1 (health, REST, SignalR) and, when set,
    /// <c>Ports:Grpc</c> as HTTP/2 only (gRPC without TLS needs an HTTP/2-only endpoint).
    /// Development binds to localhost only; other environments (containers) bind to all interfaces.
    /// </summary>
    public static WebApplicationBuilder ConfigureSmartCityKestrel(this WebApplicationBuilder builder)
    {
        var httpPort = builder.Configuration.GetValue<int?>("Ports:Http")
            ?? throw new InvalidOperationException("Missing configuration value Ports:Http");
        var grpcPort = builder.Configuration.GetValue<int?>("Ports:Grpc");
        var localOnly = builder.Environment.IsDevelopment();

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            Listen(kestrel, httpPort, HttpProtocols.Http1, localOnly);
            if (grpcPort is int port)
                Listen(kestrel, port, HttpProtocols.Http2, localOnly);
        });

        return builder;
    }

    private static void Listen(KestrelServerOptions kestrel, int port, HttpProtocols protocols, bool localOnly)
    {
        if (localOnly)
            kestrel.ListenLocalhost(port, o => o.Protocols = protocols);
        else
            kestrel.ListenAnyIP(port, o => o.Protocols = protocols);
    }
}
