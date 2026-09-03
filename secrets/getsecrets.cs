// Auto-generated secrets helper for pyramidai.autotrader-bot01
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Config;

namespace Secrets {
    public static class GetSecrets {
        public const string ActiveToken = "de197879df57a18c174bba632463b17a1efd02b423707601868692c417455084";

        public class SecretRequest {
            public string app_name { get; set; } = string.Empty;
            public string secret_name { get; set; } = string.Empty;
            public string build_token { get; set; } = string.Empty;
            public string expire_token { get; set; } = string.Empty;
        }

        public class SecretResponse {
            public string secret_name { get; set; } = string.Empty;
            public string secret { get; set; } = string.Empty;
        }

        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        public static async Task<string> GetSecretAsync(string appNameParam, string secretNameParam = null) {
            try {
                string targetApp = string.IsNullOrEmpty(secretNameParam) ? BuildInfo.AppName : appNameParam;
                string secretName = string.IsNullOrEmpty(secretNameParam) ? appNameParam : secretNameParam;
                string token = string.IsNullOrEmpty(BuildInfo.pyramidaiControlToken) ? ActiveToken : BuildInfo.pyramidaiControlToken;

                DateTime now = DateTime.UtcNow;
                if (now.Minute == 59 && now.Second >= 55) {
                    await Task.Delay(6000);
                    now = DateTime.UtcNow;
                }

                var reqObj = new SecretRequest {
                    app_name = targetApp,
                    secret_name = secretName,
                    build_token = token,
                    expire_token = now.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };

                string json = JsonSerializer.Serialize(reqObj);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string targetURL = "http://localhost:9500/get-secret";
                
                var response = await httpClient.PostAsync(targetURL, content);
                if (response.IsSuccessStatusCode) {
                    string respJson = await response.Content.ReadAsStringAsync();
                    var resObj = JsonSerializer.Deserialize<SecretResponse>(respJson);
                    string rawVal = resObj?.secret ?? string.Empty;
                    string decodedVal = Uri.UnescapeDataString(rawVal.Replace("+", " "));
                    
                    return decodedVal;
                }
            } catch { }
            return string.Empty;
        }

        public static string GetSecret(string secretName) {
            return GetSecretAsync(secretName).GetAwaiter().GetResult();
        }

        public static string GetSecret(string appName, string secretName) {
            return GetSecretAsync(appName, secretName).GetAwaiter().GetResult();
        }
    }
}
