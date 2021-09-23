using Grpc.Core;
using Grpc.Net.Client;
using Lnrpc;
using Invoicesrpc;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace LnUtils
{
    public static class Extensions
    {
        public static string ToHex(this byte[] arr)
        {
            return BitConverter.ToString(arr)
                               .ToLower()
                               .Replace("-", "");
        }

        public static string ToHex(this IEnumerable<byte> arr)
        {
            return arr.ToArray().ToHex();
        }

        public static byte[] HexToByteArray(this string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
    }

    class LnRpc
    {
        private readonly GrpcChannel channel;

        public readonly Lightning.LightningClient Ln;
        public readonly Invoices.InvoicesClient Inv;

        public LnRpc() :
            this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + Path.DirectorySeparatorChar + ".lnd") { } 

        public LnRpc(string lndDir)
        {
            channel = CreateChannel(lndDir);

            this.Ln = new Lightning.LightningClient(channel);
            this.Inv = new Invoices.InvoicesClient(channel);
        }

        private static GrpcChannel CreateChannel(string lndDir)
        {
            // Due to updated ECDSA generated tls.cert we need to let gprc know that
            // we need to use that cipher suite otherwise there will be a handshake
            // error when we communicate with the lnd rpc server.
            System.Environment.SetEnvironmentVariable("GRPC_SSL_CIPHER_SUITES", "HIGH+ECDSA");

            byte[] rawCert = File.ReadAllBytes(lndDir + "/tls.cert");
            X509Certificate2 x509Cert = new X509Certificate2(rawCert);

            string macaroon = File.ReadAllBytes(lndDir + "/data/chain/bitcoin/mainnet/admin.macaroon").ToHex();

            // add the macaroon auth credentials using an interceptor
            // so every call is properly authenticated
            Task AddMacaroon(AuthInterceptorContext context, Metadata metadata)
            {
                metadata.Add(new Metadata.Entry("macaroon", macaroon));
                return Task.CompletedTask;
            }

            Grpc.Net.Client.GrpcChannelOptions channelOptions = new GrpcChannelOptions
            {
                HttpHandler = new HttpClientHandler
                {
                    // HttpClientHandler will validate certificate chain trust by default. This won't work for a self-signed cert.
                    // Therefore validate the certificate directly
                    ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors)
                        => x509Cert.Equals(cert)
                },
                Credentials = ChannelCredentials.Create(new SslCredentials(), CallCredentials.FromInterceptor(AddMacaroon))
            };

            return GrpcChannel.ForAddress("https://localhost:10009", channelOptions);

        }
    }
}
