using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace New_Http_Trigger_Splunk
{
    public class Utils
    {
        static string splunkCertThumbprint { get; set; }

        static Utils()
        {
            splunkCertThumbprint = getEnvironmentVariable("splunkCertThumbprint");
        }

        public static string getEnvironmentVariable(string name)
        {
            var result = System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (result == null)
                return "";

            return result;
        }

        public static string getFilename(string basename)
        {

            var filename = "";
            var home = getEnvironmentVariable("HOME");
            if (home.Length == 0)
            {
                filename = "../../../" + basename;
            }
            else
            {
                filename = home + "\\site\\wwwroot\\" + basename;
            }
            return filename;
        }

        public static Dictionary<string, string> GetDictionary(string filename)
        {
            Dictionary<string, string> dictionary;
            try
            {
                string json = File.ReadAllText(filename);

                dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            catch (Exception)
            {
                dictionary = new Dictionary<string, string>();
                throw;
            }

            return dictionary;
        }

        public static string GetDictionaryValue(string key, Dictionary<string, string> dictionary)
        {
            string value = "";
            if (dictionary.TryGetValue(key, out value))
            {
                return value;
            } else
            {
                return null;
            }
        }

        public class SingleHttpClientInstance
        {
            private static readonly HttpClient HttpClient;

            static SingleHttpClientInstance()
            {
                var handler = new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = ValidateMyCert,
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
                    }
                };

                HttpClient = new HttpClient(handler);
            }

            public static async Task<HttpResponseMessage> SendToService(HttpRequestMessage req)
            {
                HttpResponseMessage response = await HttpClient.SendAsync(req);
                return response;
            }
        }

        public static bool ValidateMyCert(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslErr)
        {
            // if user has not configured a cert, anything goes
            if (string.IsNullOrWhiteSpace(splunkCertThumbprint))
                return true;

            // if user has configured a cert, must match
            var numcerts = chain.ChainElements.Count;
            var cacert = chain.ChainElements[numcerts - 1].Certificate;

            var thumbprint = cacert.GetCertHashString().ToLower();
            if (thumbprint == splunkCertThumbprint)
                return true;

            return false;
        }

        public static async Task sendViaHEC(string standardizedEvents, ILogger log)
        {
            string splunkAddress = Utils.getEnvironmentVariable("splunkAddress");
            string splunkToken = Utils.getEnvironmentVariable("splunkToken");
            if (splunkAddress.Length == 0 || splunkToken.Length == 0)
            {
                log.LogError("Values for splunkAddress and splunkToken are required.");
                throw new ArgumentException();
            }

            log.LogInformation("sending data to splunk add : " + splunkAddress);

            if (!string.IsNullOrWhiteSpace(splunkCertThumbprint))
            {
                if (!splunkAddress.ToLower().StartsWith("https"))
                {
                    throw new ArgumentException("Having provided a Splunk cert thumbprint, the address must be https://whatever");
                }
            }

            //ServicePointManager.Expect100Continue = true;
            //ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            //ServicePointManager.ServerCertificateValidationCallback += new RemoteCertificateValidationCallback(ValidateMyCert);

            var client = new SingleHttpClientInstance();
            string itemToSend = "{\"event\": " + standardizedEvents + ", \"sourcetype\": \"azure\"}";
            log.LogInformation("Sending data to splunk : " + itemToSend);
            try
            {
                var httpRequestMessage = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(splunkAddress),
                    Headers = {
                            { HttpRequestHeader.Authorization.ToString(), "Splunk " + splunkToken }
                        },
                    Content = new StringContent(itemToSend, Encoding.UTF8, "application/json")
                };

                HttpResponseMessage response = await SingleHttpClientInstance.SendToService(httpRequestMessage);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new System.Net.Http.HttpRequestException($"StatusCode from Splunk: {response.StatusCode}, and reason: {response.ReasonPhrase}");
                }
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                throw new System.Net.Http.HttpRequestException("Sending to Splunk. Is Splunk service running?", e);
            }
            catch (Exception f)
            {
                throw new System.Exception("Sending to Splunk. Unplanned exception.", f);
            }
        }

    }
}
