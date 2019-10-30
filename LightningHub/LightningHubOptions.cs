using NBitcoin;

namespace LightningHub
{
    public class LightningHubOptions
    {
        public string CryptoCode { get; set; } = "BTC";
        public NetworkType NetworkType { get; set; } = NetworkType.Mainnet;
        public string LightningConnectionString { get; set; }
    }
}