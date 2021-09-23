using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using Lnrpc;
using LnUtils;

namespace charge_multi
{
    class Program
    {
        static void Main(string[] args)
        {
            string _config = "config.json";
            if (args.Count() > 0 && File.Exists(args[0]))
                _config = args[0];
            _config = File.ReadAllText(_config);

            var config = JsonDocument.Parse(_config).RootElement;

            var c = new LnRpc();

            var channels = new List<string>();
            var channels_multi = new List<string>();

            var listChannelsResponse = c.Ln.ListChannels(new ListChannelsRequest());
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
            File.WriteAllLines(config.GetProperty("listFile").GetString(), channels_multi);

            var chargeConf = File.CreateText(config.GetProperty("confFile").GetString());

            foreach (var multi_chan_node in channels_multi)
            {
                var listChannelsRequest = new ListChannelsRequest();
                listChannelsRequest.Peer = Google.Protobuf.ByteString.CopyFrom(multi_chan_node.HexToByteArray());
                listChannelsResponse = c.Ln.ListChannels(listChannelsRequest);

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
                    uint maxFee = config.GetProperty("discourage").GetProperty("maxFee").GetUInt32();
                    uint minFee = config.GetProperty("discourage").GetProperty("minFee").GetUInt32();

                    uint fee = (uint)(minFee + ((maxFee - minFee) * (1 - ratio)));

                    Console.WriteLine("Strategy: discourage, Fee: " + fee);
                    
                    chargeConf.WriteLine("[multi-discourage-" + multi_chan_node.Substring(0, 16) + "]");
                    chargeConf.WriteLine("#ratio = " + ratio.ToString("N4"));
                    chargeConf.WriteLine("strategy = static");
                    chargeConf.WriteLine("node.id = " + multi_chan_node);
                    chargeConf.WriteLine("fee_ppm = " + fee);
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
                    uint maxFee = config.GetProperty("encourage").GetProperty("maxFee").GetUInt32();
                    uint minFee = config.GetProperty("encourage").GetProperty("minFee").GetUInt32();

                    uint fee = (uint)(minFee + ((maxFee - minFee) * (1 - ratio)));

                    Console.WriteLine("Strategy: encourage, Fee: " + fee);

                    chargeConf.WriteLine("[multi-encourage-" + multi_chan_node.Substring(0, 16) + "]");
                    chargeConf.WriteLine("#ratio = " + ratio.ToString("N4"));
                    chargeConf.WriteLine("strategy = static");
                    chargeConf.WriteLine("node.id = " + multi_chan_node);
                    chargeConf.WriteLine("fee_ppm = " + fee);
                    chargeConf.WriteLine();
                }

            }

            chargeConf.Flush();
            chargeConf.Close();

        }
    }
}
