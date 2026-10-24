namespace Collabhost.Api.Authorization;

// Endpoint metadata marking an endpoint that may take its key from the ?key= query string instead
// of the X-User-Key header. A browser EventSource cannot set request headers, so the live log
// stream needs this. The auth middleware honors the query string only when the endpoint routing
// selected carries this marker -- never because a URL happens to look like a stream route -- so an
// endpoint added later gets query-string auth only by asking for it.
//
// Sealed because attribute lookups are faster on sealed types (CA1813), and nothing extends a marker.
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowQueryStringKeyAttribute : Attribute;
