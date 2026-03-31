using System.Text.Json.Nodes;

namespace Collabhost.Api.Common;

public static class JsonObjectExtensions
{
    extension(JsonObject json)
    {
        public bool IsEmptyObject() => json.Count == 0;
    }
}
