using Newtonsoft.Json;

namespace Djia
{
    public class TickerSymbol
    {
        [JsonProperty("symbolTicker")]
        public string Symbol { get; set; }

        [JsonProperty("instrumentType")]
        public string InstrumentType { get; set; }

        [JsonProperty("instrumentName")]
        public string CompanyName { get; set; }

        [JsonProperty("exchangeId")]
        public string ExchangeId { get; set; }
    }
}
