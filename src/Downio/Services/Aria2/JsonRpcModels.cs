using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Downio.Services.Aria2;

public class JsonRpcRequest
{
    public string jsonrpc { get; set; } = "2.0";
    public string id { get; set; } = "";
    public string method { get; set; } = "";
    public JsonArray @params { get; set; } = new();
}

public class JsonRpcResponse<T>
{
    public string jsonrpc { get; set; } = "";
    public string id { get; set; } = "";
    public T? result { get; set; }
    public JsonRpcError? error { get; set; }
}

public class JsonRpcErrorResponse
{
    public string jsonrpc { get; set; } = "";
    public string id { get; set; } = "";
    public JsonRpcError? error { get; set; }
}

public class JsonRpcError
{
    public int code { get; set; }
    public string message { get; set; } = "";
}
