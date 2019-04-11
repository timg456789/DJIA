using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using AwsTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Djia
{
    public class Class1
    {
        private ITestOutputHelper Output { get; set; }

        public Class1(ITestOutputHelper output)
        {
            Output = output;
        }

        private const string TICKER_SYMBOL_PATH = "../../../../../TickerSymbols.json";
        
        private const string INSTRUMENT_TYPE_STOCK = "COMMON_STOCK";
        private const string INDEX_SORT_BY_MARKET_CAP = "instrumentType-marketCap-index";

        [Fact]
        public void DownloadTickerSymbols()
        {
            var client = new HttpClient();
            var data = new JObject();
            data.Add("instrumentType", "EQUITY");
            data.Add("pageNumber", 1);
            data.Add("sortColumn", "NORMALIZED_TICKER");
            data.Add("sortOrder", "ASC");
            data.Add("maxResultsPerPage", "10000");
            data.Add("filterToken", "");
            var tickerUrl = "https://www.nyse.com/api/quotes/filter";
            var result = client.PostAsync(tickerUrl,
                new StringContent(data.ToString(), Encoding.UTF8, "application/json")).Result;
            result.EnsureSuccessStatusCode();
            var dataOut = result.Content.ReadAsStringAsync().Result;
            var jsonOut = JArray.Parse(dataOut);
            var tickerSample = jsonOut[0];
            Assert.Equal(jsonOut.Count, tickerSample["total"].Value<int>());
            File.WriteAllText(TICKER_SYMBOL_PATH, dataOut, Encoding.UTF8);
            Output.WriteLine($"Download {jsonOut.Count} ticker symbols from {tickerUrl}");
        }

        private List<TickerSymbol> GetTickerSymbols(string type)
        {
            var data = JsonConvert.DeserializeObject<List<TickerSymbol>>(
                File.ReadAllText(TICKER_SYMBOL_PATH, Encoding.UTF8));
            return data.Where(x => x.InstrumentType.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static readonly RegionEndpoint HOME_REGION = RegionEndpoint.USEast1;

        public static AmazonDynamoDBClient DynamoDbClient => new AmazonDynamoDBClient(
            CreateCredentialsFromDefaultProfile(),
            HOME_REGION);

        private static AWSCredentials CreateCredentialsFromDefaultProfile()
        {
            var chain = new CredentialProfileStoreChain();
            var profile = "income-calculator";
            if (!chain.TryGetAWSCredentials(profile, out AWSCredentials awsCredentials))
            {
                throw new Exception($"credentials not found for \"{profile}\" profile.");
            }
            return awsCredentials;
        }

        class Logger : ILogging
        {
            private ITestOutputHelper TestLogger { get; }

            public Logger(ITestOutputHelper logger)
            {
                TestLogger = logger;
            }

            public void Log(string message)
            {
                TestLogger.WriteLine(message);
            }
        }

        [Fact]
        public void SetSymbolQuotes()
        {
            var stocks = GetTickerSymbols(INSTRUMENT_TYPE_STOCK);
            var token = Environment.GetEnvironmentVariable("IEX_Cloud_Secret_Key");

            var client = new HttpClient {BaseAddress = new Uri("https://cloud.iexapis.com/")};
            var awsClient = new AwsTools.DynamoDbClient<Quote>(DynamoDbClient, new Logger(Output));

            foreach (var stock in stocks)
            {
                var url = $"beta/stock/{stock.Symbol.ToLower()}/quote?token={token}";
                string quote = string.Empty;
                try
                {
                    var request = client.GetAsync(url).Result;
                    if (request.StatusCode == HttpStatusCode.NotFound)
                    {
                        Output.WriteLine($"{stock.Symbol} not found");
                        continue;
                    }
                    request.EnsureSuccessStatusCode();
                    quote = request.Content.ReadAsStringAsync().Result;
                    var quoteEntity = JsonConvert.DeserializeObject<Quote>(quote);
                    quoteEntity.InstrumentType = INSTRUMENT_TYPE_STOCK;
                    awsClient.Insert(quoteEntity).Wait();
                    System.Threading.Thread.Sleep(250);
                }
                catch (Exception)
                {
                    Output.WriteLine(url);
                    Output.WriteLine(JsonConvert.SerializeObject(stock, Formatting.Indented));
                    Output.WriteLine(quote);
                    throw;
                }
            }
        }

        [Fact]
        public void GetDjia()
        {
            var request = new QueryRequest(new Quote().GetTable())
            {
                IndexName = INDEX_SORT_BY_MARKET_CAP,
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>(),
                ExpressionAttributeNames = new Dictionary<string, string>(),
                Limit = 30
            };
            var key1 = "marketCap";
            string key2 = "#source" + key1;
            string key3 = ":" + key1;
            request.ExpressionAttributeNames.Add(key2, key1);
            request.ExpressionAttributeValues.Add(key3, new AttributeValue { N = "1"});
            if (!string.IsNullOrWhiteSpace(request.KeyConditionExpression))
                request.KeyConditionExpression += " and ";
            QueryRequest queryRequest = request;
            queryRequest.KeyConditionExpression = queryRequest.KeyConditionExpression + key2 + " = " + key3;
            Output.WriteLine(queryRequest.KeyConditionExpression);

            var scanResult = DynamoDbClient.QueryAsync(request).Result;
            var quotes = Conversion<Quote>.ConvertToPoco(scanResult.Items);
            Output.WriteLine(JsonConvert.SerializeObject(quotes.Select(x => x.Symbol + " " + x.CompanyName), Formatting.Indented));
        }

        [Fact]
        public void GetDjia2()
        {
            List<string> excludedSymbols = new List<string>
            {
                "FB", "BRK.B", "BAC", "MA", "CMCSA", "C", "ABT", "LLY", "ADBE", "ABBV", "CRM",
                "AVGO", "AMGN", "MDT", "HON", "ACN", "COST"
            };
            var request = new QueryRequest(new Quote().GetTable())
            {
                IndexName = INDEX_SORT_BY_MARKET_CAP,
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>(),
                ExpressionAttributeNames = new Dictionary<string, string>(),
                FilterExpression = "#sourcelatestPrice < :maxPrice",
                Limit = 1000,
                ScanIndexForward = false
            };
            foreach (var excludedSymbol in excludedSymbols)
            {
                request.FilterExpression += " and #sourceSymbol <> :" + excludedSymbol.Replace(".", "ABC123UNIQUE");
                request.ExpressionAttributeValues.Add(":" + excludedSymbol.Replace(".", "ABC123UNIQUE"), new AttributeValue { S = excludedSymbol});
            }
            request.ExpressionAttributeNames.Add("#sourceinstrumentType", "instrumentType");
            request.ExpressionAttributeNames.Add("#sourcelatestPrice", "latestPrice");
            request.ExpressionAttributeNames.Add("#sourceSymbol", "symbol");
            request.ExpressionAttributeValues.Add(":instrumentType", new AttributeValue { S = INSTRUMENT_TYPE_STOCK });
            request.ExpressionAttributeValues.Add(":maxPrice", new AttributeValue { N = "1000"});
            QueryRequest queryRequest = request;
            queryRequest.KeyConditionExpression = queryRequest.KeyConditionExpression + "#sourceinstrumentType = :instrumentType";

            var scanResult = DynamoDbClient.QueryAsync(request).Result;
            var quotes = Conversion<Quote>.ConvertToPoco(scanResult.Items);
            Output.WriteLine(
                JsonConvert.SerializeObject(quotes.Take(30).Select(x => RightPad(x.Symbol, 6) + " " + x.MarketCap + " " + x.CompanyName + " - " + x.LatestPrice), Formatting.Indented));
        }

        public string RightPad(string value, int fixedWidth)
        {
            var newValue = value + new string(' ', fixedWidth - value.Length);
            return newValue;
        }
    }
}
