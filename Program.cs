using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using Grpc.Core;
using Lnrpc;

namespace charge_multi
{
    class Program
    {
        static string homeDir = "";

        static string lndDir
        {
            get
            {
                return homeDir + "/.lnd";
            }
        }

        static Lightning.LightningClient client;

        static string ByteArrayToString(byte[] arr)
        {
            var str = BitConverter.ToString(arr);
            return str.ToLower().Replace("-", "");
        }
        static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        static void clientInit()
        {
            // Due to updated ECDSA generated tls.cert we need to let gprc know that
            // we need to use that cipher suite otherwise there will be a handshake
            // error when we communicate with the lnd rpc server.
            System.Environment.SetEnvironmentVariable("GRPC_SSL_CIPHER_SUITES", "HIGH+ECDSA");

            var cert = File.ReadAllText(lndDir + "/tls.cert");
            byte[] macaroonBytes = File.ReadAllBytes(lndDir + "/data/chain/bitcoin/mainnet/admin.macaroon");
            var macaroon = ByteArrayToString(macaroonBytes);

            var sslCreds = new SslCredentials(cert);

            // combine the cert credentials and the macaroon auth credentials using interceptors
            // so every call is properly encrypted and authenticated
            Task AddMacaroon(AuthInterceptorContext context, Metadata metadata)
            {
                metadata.Add(new Metadata.Entry("macaroon", macaroon));
                return Task.CompletedTask;
            }
            var macaroonInterceptor = new AsyncAuthInterceptor(AddMacaroon);
            var combinedCreds = ChannelCredentials.Create(sslCreds, CallCredentials.FromInterceptor(macaroonInterceptor));

            var channel = new Grpc.Core.Channel("localhost:10009", combinedCreds);
            client = new Lnrpc.Lightning.LightningClient(channel);
        }


        static void Main(string[] args)
        {
            string _config = "config.json";
            if (args.Count() > 0 && File.Exists(args[0]))
                _config = args[0];
            _config = File.ReadAllText(_config);

            var config = JsonDocument.Parse(_config).RootElement;
            

            homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            clientInit();

            var channels = new List<string>();
            var channels_multi = new List<string>();

            var listChannelsResponse = client.ListChannels(new ListChannelsRequest());
            foreach (var channel in listChannelsResponse.Channels)
            {
                if (!channels.Contains(channel.RemotePubkey))
                {
                    channels.Add(channel.RemotePubkey);
                }
                else
                {
                    if (!channels_multi.Contains(channel.RemotePubkey))
                    {
                        channels_multi.Add(channel.RemotePubkey);
                        Console.WriteLine("Node " + channel.RemotePubkey + " has multiple channels.");
                    }
                }
            }
            Console.WriteLine();

            Console.WriteLine("Writing channel list to " + config.GetProperty("listFile").GetString());
            File.WriteAllLines("multiple_channels.list", channels_multi);

            var chargeConf = File.CreateText(config.GetProperty("confFile").GetString());

            foreach (var multi_chan_node in channels_multi)
            {
                var listChannelsRequest = new ListChannelsRequest();
                listChannelsRequest.Peer = Google.Protobuf.ByteString.CopyFrom(StringToByteArray(multi_chan_node));
                listChannelsResponse = client.ListChannels(listChannelsRequest);

                long totalBalance = 0;
                long totalLocalBalance = 0;
                long totalRemoteBalance = 0;

                foreach (var channel in listChannelsResponse.Channels)
                {
                    totalBalance += channel.Capacity;
                    totalLocalBalance += channel.LocalBalance;
                    totalRemoteBalance += channel.RemoteBalance;
                }

                decimal ratio = ((decimal)totalLocalBalance) / ((decimal)totalBalance);

                Console.WriteLine(multi_chan_node + " - overall ratio: " + ratio.ToString("N4"));

                if (config.GetProperty("disable").GetProperty("enabled").GetBoolean() 
                    && ratio < config.GetProperty("disable").GetProperty("maxRatio").GetDecimal())
                {
                    Console.WriteLine("Strategy: disable");

                    chargeConf.WriteLine("[multi-disable-" + multi_chan_node.Substring(0,16) + "]");
                    chargeConf.WriteLine("#ratio = " + ratio.ToString("N4"));
                    chargeConf.WriteLine("strategy = disable");
                    chargeConf.WriteLine("node.id = " + multi_chan_node);
                    chargeConf.WriteLine();
                }
                else if (config.GetProperty("discourage").GetProperty("enabled").GetBoolean() 
                    && ratio >= config.GetProperty("discourage").GetProperty("minRatio").GetDecimal() 
                    && ratio < config.GetProperty("discourage").GetProperty("maxRatio").GetDecimal())
                {
                    Console.WriteLine("Strategy: discourage, Fee: " + config.GetProperty("discourage").GetProperty("feeRate").GetUInt32());
                    
                    chargeConf.WriteLine("[multi-discourage-" + multi_chan_node.Substring(0, 16) + "]");
                    chargeConf.WriteLine("#ratio = " + ratio.ToString("N4"));
                    chargeConf.WriteLine("strategy = static");
                    chargeConf.WriteLine("node.id = " + multi_chan_node);
                    chargeConf.WriteLine("fee_ppm = " + config.GetProperty("discourage").GetProperty("feeRate").GetUInt32());
                    chargeConf.WriteLine();
                }
                else if (config.GetProperty("proportional").GetProperty("enabled").GetBoolean()
                    && ratio >= config.GetProperty("proportional").GetProperty("minRatio").GetDecimal()
                    && ratio < config.GetProperty("proportional").GetProperty("maxRatio").GetDecimal())
                {
                    uint maxFee = config.GetProperty("proportional").GetProperty("maxFee").GetUInt32();
                    uint minFee = config.GetProperty("proportional").GetProperty("minFee").GetUInt32();

                    uint fee = (uint)(minFee + ((maxFee - minFee) * (1 - ratio)));

                    Console.WriteLine("Strategy: proportional, Fee: " + fee);

                    chargeConf.WriteLine("[multi-proportional-" + multi_chan_node.Substring(0, 16) + "]");
                    chargeConf.WriteLine("#ratio = " + ratio.ToString("N4"));
                    chargeConf.WriteLine("strategy = static");
                    chargeConf.WriteLine("node.id = " + multi_chan_node);
                    chargeConf.WriteLine("fee_ppm = " + fee);
                    chargeConf.WriteLine();
                }
                else if (config.GetProperty("encourage").GetProperty("enabled").GetBoolean()
                    && ratio >= config.GetProperty("encourage").GetProperty("minRatio").GetDecimal())
                {
                    Console.WriteLine("Strategy: encourage, Fee: " + config.GetProperty("encourage").GetProperty("feeRate").GetUInt32());

                    chargeConf.WriteLine("[multi-encourage-" + multi_chan_node.Substring(0, 16) + "]");
                    chargeConf.WriteLine("#ratio = " + ratio.ToString("N4"));
                    chargeConf.WriteLine("strategy = static");
                    chargeConf.WriteLine("node.id = " + multi_chan_node);
                    chargeConf.WriteLine("fee_ppm = " + config.GetProperty("encourage").GetProperty("feeRate").GetUInt32());
                    chargeConf.WriteLine();
                }

            }

            chargeConf.Flush();
            chargeConf.Close();

        }
    }
}
