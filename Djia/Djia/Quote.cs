using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;
using AwsTools;
using Newtonsoft.Json;

namespace Djia
{
    public class Quote : IModel
    {
        [JsonProperty("marketCap")]
        public decimal? MarketCap { get; set; }
        [JsonProperty("latestPrice")]
        public decimal? LatestPrice { get; set; }
        [JsonProperty("symbol")]
        public string Symbol { get; set; }
        [JsonProperty("companyName")]
        public string CompanyName { get; set; }
        [JsonProperty("instrumentType")]
        public string InstrumentType { get; set; }

        public Dictionary<string, AttributeValue> GetKey()
        {
            return new Dictionary<string, AttributeValue>
            {
                {"symbol", new AttributeValue {S = Symbol}}
            };
        }

        public string GetTable()
        {
            return "stock-quotes";
        }
    }
}
