using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Common.Messaging;

// Self-signed cert management for the AccountServer<->GameServer TLS channel.
// There's no CA here - trust is established by pinning the exact certificate
// (by thumbprint) rather than validating a certificate chain, since these are
// two servers we control, not a public-facing endpoint.
public static class RpcCertificateHelper {
    // Config values are relative paths (matching ConfigLoader's own convention) -
    // resolve them against the running assembly's directory, not the process's
    // current working directory, which isn't guaranteed to match.
    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    public static X509Certificate2 LoadOrCreateServerCertificate(string pfxPath, string password) {
        pfxPath = ResolvePath(pfxPath);

        if (File.Exists(pfxPath))
            return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=WaWRpcServer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

        var dir = Path.GetDirectoryName(pfxPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx, password));

        // Public half only, no private key - this is the file to copy over to
        // whatever machine GameServer runs on, so it can pin/trust this cert.
        var cerPath = Path.ChangeExtension(pfxPath, ".cer");
        File.WriteAllBytes(cerPath, cert.Export(X509ContentType.Cert));

        return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password);
    }

    public static X509Certificate2 LoadTrustedCertificate(string cerPath) {
        cerPath = ResolvePath(cerPath);

        if (!File.Exists(cerPath))
            throw new FileNotFoundException(
                $"Trusted RPC certificate not found at '{cerPath}'. Copy the AccountServer's " +
                "generated rpc-server.cer here (see RpcServerConfig.CertificatePfxPath's directory " +
                "on the machine running AccountServer).", cerPath);

        return X509CertificateLoader.LoadCertificateFromFile(cerPath);
    }

    public static bool ValidatePinned(X509Certificate2 trustedCert, object sender, X509Certificate certificate,
        X509Chain chain, SslPolicyErrors sslPolicyErrors) {
        return certificate != null && certificate.GetCertHashString() == trustedCert.Thumbprint;
    }
}
