using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Downio.Services;

namespace Downio.Services.Aria2;

public class JsonRpcClient
{
    private readonly HttpClient _httpClient;
    private readonly string _rpcUrl;
    private readonly string _secret;

    public JsonRpcClient(string rpcUrl, string secret = "")
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            UseProxy = false,
            Proxy = null
        })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _rpcUrl = rpcUrl;
        _secret = secret;
    }

    public async Task<T?> InvokeAsync<T>(string method, params object[] args)
    {
        var paramArray = new JsonArray();
        if (!string.IsNullOrEmpty(_secret))
        {
            paramArray.Add((JsonNode?)JsonValue.Create($"token:{_secret}"));
        }
        
        if (args != null)
        {
            foreach (var arg in args)
            {
                if (arg == null) paramArray.Add((JsonNode?)null);
                else if (arg is string s) paramArray.Add((JsonNode?)JsonValue.Create(s));
                else if (arg is int i) paramArray.Add((JsonNode?)JsonValue.Create(i));
                else if (arg is string[] sa) 
                {
                    var arr = new JsonArray();
                    foreach(var item in sa) arr.Add((JsonNode?)JsonValue.Create(item));
                    paramArray.Add((JsonNode)arr);
                }
                else if (arg is IEnumerable<string> es)
                {
                    var arr = new JsonArray();
                    foreach(var item in es) arr.Add((JsonNode?)JsonValue.Create(item));
                    paramArray.Add((JsonNode)arr);
                }
                else if (arg is Dictionary<string, string> dict)
                {
                    var obj = new JsonObject();
                    foreach(var kvp in dict) obj[kvp.Key] = JsonValue.Create(kvp.Value);
                    paramArray.Add((JsonNode)obj);
                }
                else
                {
                    Debug.WriteLine($"Warning: Unknown arg type {arg.GetType()} in RPC call");
                }
            }
        }

        var request = new JsonRpcRequest
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = $"aria2.{method}",
            @params = paramArray
        };

        var json = JsonSerializer.Serialize(request, Aria2JsonContext.Default.JsonRpcRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_rpcUrl, content).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseJson);
            
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.GetProperty("code").GetInt32();
                var message = error.GetProperty("message").GetString();
                throw new Exception($"Aria2 RPC Error {code}: {message}");
            }

            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                if (typeof(T) == typeof(string))
                {
                    // string can be deserialized directly or just taken
                    return (T)(object)(result.GetString() ?? "");
                }

                if (typeof(T) == typeof(List<Aria2TaskStatus>))
                {
                    return (T)(object)JsonSerializer.Deserialize(result.GetRawText(), Aria2JsonContext.Default.ListAria2TaskStatus)!;
                }

                if (typeof(T) == typeof(Dictionary<string, string>))
                {
                    return (T)(object)JsonSerializer.Deserialize(result.GetRawText(), Aria2JsonContext.Default.DictionaryStringString)!;
                }

                // Fallback for types not explicitly handled but registered?
                // But we only use these two.
                throw new NotSupportedException($"Type {typeof(T)} is not supported in AOT RPC client.");
            }

            return default;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RPC Call Failed: {method} - {ex.Message}");
            AppLog.Error(ex, $"RPC Call Failed: {method}");
            throw;
        }
    }
}
