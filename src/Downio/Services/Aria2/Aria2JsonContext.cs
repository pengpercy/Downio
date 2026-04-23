using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Downio.Services.Aria2;

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse<string>))]
[JsonSerializable(typeof(JsonRpcResponse<List<Aria2TaskStatus>>))]
[JsonSerializable(typeof(JsonRpcErrorResponse))]
[JsonSerializable(typeof(List<Aria2TaskStatus>))]
[JsonSerializable(typeof(Aria2TaskStatus))]
[JsonSerializable(typeof(Aria2File))]
[JsonSerializable(typeof(Aria2Uri))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class Aria2JsonContext : JsonSerializerContext
{
}
