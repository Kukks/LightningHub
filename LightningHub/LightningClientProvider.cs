using System;
using System.Linq;
using BTCPayServer.Lightning;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace LightningHub
{
    public class LightningClientProvider
    {
        private readonly ILightningClient _lightningClient;
        private readonly Network _network;

        public LightningClientProvider(IOptions<LightningHubOptions> options)
        {
            if (!LightningConnectionString.TryParse(options.Value.LightningConnectionString, out var connectionString))
            {
                throw new Exception("Connection string was invalid");
            }

            var networkSets = NBitcoin.Network.GetNetworks().Select(network => network.NetworkSet).Distinct();

            var networkSet = networkSets.Single(set =>
                set.CryptoCode.Equals(options.Value.CryptoCode, StringComparison.InvariantCultureIgnoreCase));

            _network = networkSet.GetNetwork(options.Value.NetworkType);
            _lightningClient = LightningClientFactory.CreateClient(connectionString, _network);
        }

        public ILightningClient GetLightningClient()
        {
            return _lightningClient;
        }

        public Network GetNetwork()
        {
            return _network;
        }
    }
}