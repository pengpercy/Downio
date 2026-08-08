using System.Text.Json.Serialization;

namespace Downio.Models;

[JsonConverter(typeof(JsonStringEnumConverter<UpdateChannel>))]
public enum UpdateChannel
{
    Stable,
    Beta
}
