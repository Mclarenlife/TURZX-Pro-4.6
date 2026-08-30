using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Turzx.Desktop.Models;

namespace Turzx.Desktop.Services
{
    internal sealed class WeatherService : IDisposable
    {
        private readonly HttpClient _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public async Task<WeatherSnapshot> ResolveAndFetchAsync(WeatherSettings settings)
        {
            string city = (settings.City ?? string.Empty).Trim();
            List<Dictionary<string, object>> candidates = new List<Dictionary<string, object>>();
            if (ShouldTryCitySuffix(city))
                candidates.AddRange(await SearchLocationsAsync(city + "市"));
            candidates.AddRange(await SearchLocationsAsync(city));
            Dictionary<string, object> location = SelectBestLocation(city, candidates);
            if (location == null) throw new InvalidOperationException("没有找到该城市");
            settings.Latitude = Convert.ToDouble(location["latitude"], CultureInfo.InvariantCulture);
            settings.Longitude = Convert.ToDouble(location["longitude"], CultureInfo.InvariantCulture);
            string name = Convert.ToString(location["name"]);
            string admin = location.ContainsKey("admin1") ? Convert.ToString(location["admin1"]) : string.Empty;
            settings.ResolvedName = string.IsNullOrWhiteSpace(admin) || admin == name ? name : admin + " · " + name;
            settings.GeocodingVersion = 2;
            return await FetchAsync(settings);
        }

        private async Task<List<Dictionary<string, object>>> SearchLocationsAsync(string query)
        {
            string geocodingUrl = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                Uri.EscapeDataString(query ?? string.Empty) +
                "&count=10&language=zh&format=json";
            string geocodingJson = await _client.GetStringAsync(geocodingUrl);
            Dictionary<string, object> root = _json.DeserializeObject(geocodingJson) as
                Dictionary<string, object>;
            object rawResults;
            object[] results = root != null && root.TryGetValue("results", out rawResults) ?
                rawResults as object[] : null;
            List<Dictionary<string, object>> locations = new List<Dictionary<string, object>>();
            if (results == null) return locations;
            foreach (object result in results)
            {
                Dictionary<string, object> location = result as Dictionary<string, object>;
                if (location != null) locations.Add(location);
            }
            return locations;
        }

        private static Dictionary<string, object> SelectBestLocation(string query,
            IList<Dictionary<string, object>> candidates)
        {
            Dictionary<string, object> best = null;
            double bestScore = double.MinValue;
            foreach (Dictionary<string, object> candidate in candidates)
            {
                string name = GetString(candidate, "name");
                string normalizedQuery = NormalizePlaceName(query);
                string normalizedName = NormalizePlaceName(name);
                double score = 0;
                if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) score += 1000;
                else if (string.Equals(normalizedName, normalizedQuery,
                    StringComparison.OrdinalIgnoreCase)) score += 850;

                string feature = GetString(candidate, "feature_code");
                if (feature == "PPLC") score += 600;
                else if (feature.StartsWith("PPLA", StringComparison.OrdinalIgnoreCase)) score += 500;
                else if (feature == "PPL") score += 50;
                if (GetString(candidate, "country_code") == "CN" && ContainsChinese(query))
                    score += 80;

                object rawPopulation;
                double population;
                if (candidate.TryGetValue("population", out rawPopulation) &&
                    rawPopulation != null && double.TryParse(Convert.ToString(rawPopulation,
                        CultureInfo.InvariantCulture), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out population) && population > 0)
                    score += Math.Min(350, Math.Log10(population + 1) * 45);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        private static bool ShouldTryCitySuffix(string value)
        {
            if (!ContainsChinese(value) || string.IsNullOrWhiteSpace(value)) return false;
            char suffix = value[value.Length - 1];
            return suffix != '市' && suffix != '县' && suffix != '区' &&
                suffix != '州' && suffix != '盟';
        }

        private static bool ContainsChinese(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char character in value)
                if (character >= '\u4e00' && character <= '\u9fff') return true;
            return false;
        }

        private static string NormalizePlaceName(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.EndsWith("市", StringComparison.Ordinal) ?
                normalized.Substring(0, normalized.Length - 1) : normalized;
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ?
                Convert.ToString(value) : string.Empty;
        }

        public async Task<WeatherSnapshot> FetchAsync(WeatherSettings settings)
        {
            string unit = string.Equals(settings.TemperatureUnit, "Fahrenheit",
                StringComparison.OrdinalIgnoreCase) ? "fahrenheit" : "celsius";
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}" +
                "&current=temperature_2m,weather_code&temperature_unit={2}&timezone=auto",
                settings.Latitude, settings.Longitude, unit);
            string json = await _client.GetStringAsync(url);
            Dictionary<string, object> root = _json.DeserializeObject(json) as Dictionary<string, object>;
            Dictionary<string, object> current = root == null ? null : root["current"] as Dictionary<string, object>;
            if (current == null) throw new InvalidOperationException("天气服务没有返回当前天气");
            double temperature = Convert.ToDouble(current["temperature_2m"], CultureInfo.InvariantCulture);
            int code = Convert.ToInt32(current["weather_code"], CultureInfo.InvariantCulture);
            return new WeatherSnapshot
            {
                LocationName = string.IsNullOrWhiteSpace(settings.City) ?
                    settings.ResolvedName : settings.City.Trim(),
                Temperature = temperature,
                WeatherCode = code,
                Condition = DescribeWeatherCode(code),
                Unit = unit == "fahrenheit" ? "°F" : "°C"
            };
        }

        private static string DescribeWeatherCode(int code)
        {
            if (code == 0) return "晴";
            if (code == 1) return "大部晴朗";
            if (code == 2) return "多云";
            if (code == 3) return "阴";
            if (code == 45 || code == 48) return "雾";
            if (code >= 51 && code <= 57) return "毛毛雨";
            if (code >= 61 && code <= 67) return "雨";
            if (code >= 71 && code <= 77) return "雪";
            if (code >= 80 && code <= 82) return "阵雨";
            if (code >= 85 && code <= 86) return "阵雪";
            if (code >= 95) return "雷暴";
            return "天气代码 " + code;
        }

        public void Dispose() { _client.Dispose(); }
    }
}
