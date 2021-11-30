using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using DecisionsFramework.Data.DataTypes;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.ServiceLayer;
using Microsoft.SqlServer.Server;
using System.ServiceModel;
using Decisions.ForEx.StrikeIronForEx;
using DecisionsFramework;
using DecisionsFramework.Utilities.Data;

namespace Decisions.ForEx
{
    [AutoRegisterMethodsOnClass(true, "Integration", "Foreign Exchange")]
    public static class ForExSteps
    {
        private static Log log = new Log("Foreign Exchange");
        
#region Foreign Exchange Steps
        public static decimal GetConversionRate(string from, string to, int validIfWithinHours = 24)
        {
            if (string.IsNullOrEmpty(from)) throw new ArgumentNullException(nameof(from), "Source currency cannot be null");
            if (string.IsNullOrEmpty(to)) throw new ArgumentNullException(nameof(to), "Destination currency cannot be null");
            
            var settings = GetSettings();
            
            // If a customer has previously configured a StrikeIron account and hasn't configured CurrencyLayer, call StrikeIron method, deprecated route
            if (!string.IsNullOrEmpty(settings.UserId) && !string.IsNullOrEmpty(settings.Password) && string.IsNullOrEmpty(settings.ApiKey))
                return GetStrikeIronConversionRate(from, to, validIfWithinHours);
                
            // use CurrencyLayer, Default route
            return GetCurrencyLayerExchangeRate(from, to, validIfWithinHours);
            
        }

        public static decimal GetConversionRateAsOfDate(string from, string to, DateTime asOfDateTime)
        {
            if (string.IsNullOrEmpty(from)) throw new ArgumentNullException(nameof(from), "Source currency cannot be null");
            if (string.IsNullOrEmpty(to)) throw new ArgumentNullException(nameof(to), "Destination currency cannot be null");
            
            var settings = GetSettings();
            
            if (!string.IsNullOrEmpty(settings.UserId) && !string.IsNullOrEmpty(settings.Password) && string.IsNullOrEmpty(settings.ApiKey))
                return GetStrikeIronConversionRateAsOfDate(from, to, asOfDateTime);
            
            return GetCurrencyLayerExchangeRate(from, to, 0, asOfDateTime);
            
        }
        
        public static decimal DoConversion(string from, string to, decimal amount, int validIfWithinHours = 24)
        {
            var rate = GetConversionRate(from, to, validIfWithinHours);

            return amount*rate;
        }
        
        public static decimal DoConversionAsOfDate(string from, string to, decimal amount, DateTime asOfDateTime)
        {
            var rate = GetConversionRateAsOfDate(from, to, asOfDateTime);

            return amount * rate;
        }
        
        public static decimal DoConversionAsFirstOfMonth(string from, string to, decimal amount, DateTime asOfDateTime)
        {
            var rate = GetConversionRateAsOfDate(from, to, new DateTime(asOfDateTime.Year, asOfDateTime.Month, 1));

            return amount * rate;
        }
        
        public static decimal GetConversionRateAsFirstOfMonth(string from, string to, decimal amount, DateTime asOfDateTime)
        {
            var rate = GetConversionRateAsOfDate(from, to, new DateTime(asOfDateTime.Year, asOfDateTime.Month, 1));

            return rate;
        }

#endregion
        
#region CurrencyLayer methods
        private static decimal GetCurrencyLayerExchangeRate(string from, string to, int validIfWithinHours = 24, DateTime? asOfDateTime = null)
        {
            if (to.Length > 3) to = to.Substring(0, 3); // Do not allow multiple destination currencies, if they are included
            
            // Return cached rates if available
            ForExRateHistory rate;
            if (asOfDateTime.HasValue)
            {
                rate = GetHistoricalRateCache().AllEntities.FirstOrDefault(r => r.AsOfDateTime.Date == asOfDateTime.Value.Date && r.FromCurrency == from && r.ToCurrency == to);
                if (rate != null) return rate.Rate;
            }
            else
            {
                rate = GetCache().AllEntities.FirstOrDefault(r => r.FromCurrency == from && r.ToCurrency == to);
                if (rate != null && validIfWithinHours > 0 && rate.AsOfDateTime > DateTime.Now.Subtract(TimeSpan.FromHours(validIfWithinHours))) return rate.Rate;
            }

            
            var settings = GetSettings();
            
            if (string.IsNullOrEmpty(settings.ApiKey))
                throw new Exception("The CurrencyLayer API Key in the Foreign Exchange Settings is not setup");

            
            // Get new rates from CurrencyLayer
            decimal dRate = 0.0M;
            using (HttpClient client = new HttpClient())
            {
                string protocol = settings.UseHttps ? "https" : "http";
                client.BaseAddress = new Uri(protocol+@"://api.currencylayer.com/live");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                string urlParams = $"?access_key={settings.ApiKey}";
                urlParams += $"&source={from}&currencies={to}";
                if (asOfDateTime != null)
                    urlParams += $"&date={asOfDateTime:yyyy-MM-dd}";

                using (HttpResponseMessage response = client.GetAsync(urlParams).Result)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonRet = response.Content.ReadAsStringAsync().Result;

                        if (JsonUtility.DoesPropertyExist(jsonRet, "error"))
                        {
                            string errorText = JsonUtility.GetValueByName(
                                JsonUtility.GetValueByName(jsonRet, "error"), 
                                "info");
                            throw new Exception(errorText);
                        }
                        else
                        {
                            dRate = Decimal.Parse(JsonUtility.GetValueByName(
                                JsonUtility.GetValueByName(jsonRet, "quotes"),
                                $"{from}{to}"));
                        }
                    }
                    else
                    {
                        throw new Exception(response.ReasonPhrase);
                    }
                }
            }

            if (rate != null)
            {
                rate.IsLatest = false;
                rate.Store();                
            }
            
            // create new rate object
            rate = new ForExRateHistory();
            rate.IsLatest = (asOfDateTime == null);
            rate.ToCurrency = to;
            rate.FromCurrency = from;
            rate.Rate = dRate;
            rate.AsOfDateTime = asOfDateTime ?? DateTime.Now;
            rate.Store();

            return rate.Rate;
        }
        
#endregion

#region Caching

        private static EntityCache<ForExRateHistory> GetCache()
        {
            return EntityCache<ForExRateHistory>.GetCache("LatestForEx", p => p.IsLatest,
                new WhereCondition[] { new FieldWhereCondition("is_latest", QueryMatchType.Equals, true), });

        }

        private static EntityCache<ForExRateHistory> GetHistoricalRateCache()
        {
            return EntityCache<ForExRateHistory>.GetCache("recentOnes", p => p.AsOfDateTime > DateTime.Now.Subtract(TimeSpan.FromDays(90)),
                new WhereCondition[] { new FieldWhereCondition("as_of_date_time", QueryMatchType.GreaterThan, DateTime.Now.Subtract(TimeSpan.FromDays(90))), });

        }

#endregion

#region StrikeIron Methods (Deprecated)

        private static decimal GetStrikeIronConversionRate(string from, string to, int validIfWithinHours = 24)
        {
            CurrencyRatesSoap client = new CurrencyRatesSoapClient(new BasicHttpBinding(), new EndpointAddress("http://wsparam.strikeiron.com/StrikeIron/ForeignExchangeRate3/CurrencyRates"));
            var rate = GetCache().AllEntities.FirstOrDefault(r => r.FromCurrency == from && r.ToCurrency == to);

            if (rate != null && 
                validIfWithinHours > 0 && 
                rate.AsOfDateTime > DateTime.Now.Subtract(TimeSpan.FromHours(validIfWithinHours)))
            {
                return rate.Rate;
            }

            var settings = GetSettings();

            log.Warn("StrikeIron services in Decisions have been deprecated. You should configure Settings for the new CurrencyLayer API.");
            if (string.IsNullOrEmpty(settings.UserId) || string.IsNullOrEmpty(settings.Password))
                throw new Exception("The StrikeIron.com credentials in the Foreign Exchange Settings are not setup");

            var rates = client.GetLatestRateAsync(new GetLatestRateRequest(null, settings.UserId, settings.Password, from, to)).Result;

            if (rates.GetLatestRateResult.ServiceStatus.StatusNbr > 300)
                throw new Exception("Service Error: " + rates.GetLatestRateResult.ServiceStatus.StatusDescription);

            // set old rate as not latest
            if (rate != null)
            {
                rate.IsLatest = false;
                rate.Store();                
            }

            // create new rate object
            rate = new ForExRateHistory();
            rate.IsLatest = true;
            rate.ToCurrency = to;
            rate.FromCurrency = from;
            rate.Rate = (decimal)rates.GetLatestRateResult.ServiceResult.Value;
            rate.AsOfDateTime = DateTime.Now;
            rate.Store();

            return rate.Rate;
        }

        private static decimal GetStrikeIronConversionRateAsOfDate(string from, string to, DateTime asOfDateTime)
        {
            var settings = GetSettings();
            CurrencyRatesSoap client = new CurrencyRatesSoapClient(new BasicHttpBinding(), new EndpointAddress("http://wsparam.strikeiron.com/StrikeIron/ForeignExchangeRate3/CurrencyRates"));
           
            var rate = GetHistoricalRateCache().AllEntities.FirstOrDefault(r => r.AsOfDateTime.Date == asOfDateTime.Date && r.FromCurrency == from && r.ToCurrency == to);

            if (rate != null)
                return rate.Rate;

            log.Warn("StrikeIron services in Decisions have been deprecated. You should configure Settings for the new CurrencyLayer API.");
            if (string.IsNullOrEmpty(settings.UserId) || string.IsNullOrEmpty(settings.Password))
                throw new Exception("The StrikeIron.com credentials in the Foreign Exchange Settings are not setup");

            var rates = client.GetHistoricalRateAsync(new GetHistoricalRateRequest(
                null,
                settings.UserId,
                settings.Password,
                from,
                to,
                asOfDateTime.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
                asOfDateTime.AddDays(1).ToString("M/d/yyyy", CultureInfo.InvariantCulture))).Result;

            if (rates.GetHistoricalRateResult.ServiceStatus.StatusNbr > 300)
                throw new Exception("Service Error: " + rates.GetHistoricalRateResult.ServiceStatus.StatusDescription);

            rate = new ForExRateHistory();
            rate.IsLatest = false;
            rate.ToCurrency = to;
            rate.FromCurrency = from;
            var rateRes = rates.GetHistoricalRateResult.ServiceResult.Results.FirstOrDefault();
            if (rateRes == null)
                throw new Exception("no rates returned");
            rate.Rate = (decimal)rateRes.Value;
            rate.AsOfDateTime = asOfDateTime;
            rate.Store();

            return rate.Rate;
        }

#endregion

        private static ForeignExchangeSettings GetSettings()
        {
            return ModuleSettingsAccessor<ForeignExchangeSettings>.GetSettings();
        }


    }

    [ORMEntity]
    public class ForExRateHistory : BaseORMEntity
    {
        [ORMPrimaryKeyField]
        private string id;
        [ORMField]
        private string fromCurrency;
        [ORMField]
        private string toCurrency;
        [ORMField]
        private decimal rate;
        [ORMField]
        private DateTime asOfDateTime;
        [ORMField]
        private bool isLatest;

        public string Id
        {
            get { return id; }
            set { id = value; }
        }

        public string FromCurrency
        {
            get { return fromCurrency; }
            set { fromCurrency = value; }
        }

        public string ToCurrency
        {
            get { return toCurrency; }
            set { toCurrency = value; }
        }

        public decimal Rate
        {
            get { return rate; }
            set { rate = value; }
        }

        public DateTime AsOfDateTime
        {
            get { return asOfDateTime; }
            set { asOfDateTime = value; }
        }

        public bool IsLatest
        {
            get { return isLatest; }
            set { isLatest = value; }
        }
    }
}
