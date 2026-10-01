using System;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace TheOtherRoles.Modules.Music
{
    public static class Http
    {
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static readonly HttpClient client = Create();

        private static HttpClient Create()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };

            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
            http.DefaultRequestHeaders.Add("Cookie", "os=pc; appver=2.9.7;");
            return http;
        }

        public static IEnumerator CoGetString(string url, Action<string, string> onDone)
        {
            Task<string> task;
            try
            {
                task = client.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                onDone?.Invoke(null, ex.Message);
                yield break;
            }

            while (!task.IsCompleted) yield return new WaitForEndOfFrame();
            if (task.IsFaulted) onDone?.Invoke(null, Describe(task));
            else onDone?.Invoke(task.Result, null);
        }

        public static IEnumerator CoGetBytes(string url, Action<byte[], string> onDone)
        {
            Task<byte[]> task;
            try
            {
                task = client.GetByteArrayAsync(url);
            }
            catch (Exception ex)
            {
                onDone?.Invoke(null, ex.Message);
                yield break;
            }

            while (!task.IsCompleted) yield return new WaitForEndOfFrame();
            if (task.IsFaulted) onDone?.Invoke(null, Describe(task));
            else onDone?.Invoke(task.Result, null);
        }

        private static string Describe(Task task)
        {
            var error = task.Exception?.GetBaseException();
            return error?.Message ?? "request failed";
        }
    }
}
