namespace Collabhost.Api.Capabilities.Configurations;

// Managed response-header injection for hosted-app routes. Card #309.
//
// Wraps every routed app's responses with a Caddy `headers` handler. The
// platform asserts a safe XCTO (`X-Content-Type-Options: nosniff`) baseline by
// default on every routed app type's type-level binding; HSTS is an explicit
// operator opt-in because its durable browser-pin semantics extend beyond
// platform-controlled state.
//
// The capability composes with the path-scoped routing.responseHeaders
// channel from card #308 (file-server only): security-headers is the blanket,
// route-wide channel; the other is the per-path, more-specific channel. Both
// are emitted into the file-server subroute; the blanket handler is inserted
// BEFORE the path-matched handlers so a path-specific operator rule wins on
// overlapping header names (CSS-specificity analogy -- more-specific wins).
//
// Empty-`Headers` + `EnableHsts: false` is the load-bearing migration safety:
// when both are absent the emission helper returns null and no `headers`
// handler is appended (and, for reverse-proxy routes, no subroute wrap is
// added). With XCTO seeded as the platform default, no routed app starts in
// the empty state post-upgrade -- the empty path matters only for the operator
// who later suppresses XCTO via the empty-string-suppression mechanism (a row
// with an empty value is dropped at emission, per #309 Bill ruling).
//
// XCTO operator suppression: emission drops entries whose value is the empty
// string. Operator who needs MIME-sniffing rescue sets
// `X-Content-Type-Options: ""` in the Headers map; the entry survives the
// MergeJson semantic (it is an override over the default value) and the
// emission helper skips it. Release-note disclosure: "set its value to empty
// to suppress emission" (NOT "delete the row" -- MergeJson is one-level-deep
// and clearing the map cannot remove a type-default entry).
//
// HSTS convenience-vs-freeform collision: ValidateEdits rejects the
// cross-field state when `EnableHsts: true` AND `Headers` carries a
// `Strict-Transport-Security` entry. The operator must choose one channel.
public class SecurityHeadersConfiguration
{
    // Operator-controlled HSTS toggle. Off by default on every type. When true,
    // the emission helper appends a `Strict-Transport-Security` entry computed
    // from `HstsMaxAgeSeconds`. The freeform `Headers` map must not also carry
    // `Strict-Transport-Security` (cross-field validation rejects the collision).
    public bool EnableHsts { get; set; }

    // HSTS max-age in seconds. Default is 300 (5 minutes) -- a deliberately
    // short staged-rollout floor that lets the operator verify HSTS works on
    // their app before raising to production durations (6 months = 15768000,
    // 1 year = 31536000, 2 years = 63072000). Zero is allowed and is the
    // documented HSTS-clear / un-pin signal that tells browsers to expire any
    // prior pinning for this origin (HSTS rollback path).
    public int HstsMaxAgeSeconds { get; set; } = 300;

    // Freeform HTTP-header-name to value map. Keys are validated against an
    // RFC 7230 tchar pattern (header-name half of #308's compound pattern).
    // Values are surfaced as a single Caddy `response.set` per emitted handler,
    // which overwrites any same-named header from the upstream. An entry whose
    // value is the empty string is operator-suppression: the emission helper
    // drops it, allowing the operator to suppress a type-level default
    // (notably the XCTO seed) without being able to "delete" the row via the
    // shipped MergeJson semantics.
    //
    // The explicit setter normalizes JSON null to an empty dictionary so an
    // override containing {"headers":null} -- which passes ValidateEdits (null
    // is not a JsonObject) and flows through MergeJson's else-branch -- never
    // reaches the emitter as a null reference. Same shape as #308's
    // RoutingConfiguration.ResponseHeaders and #336's
    // RuntimeConfigFileConfiguration.Values.
    // IDE0032: cannot use auto-property -- the setter normalizes JSON null to
    // an empty dictionary, which an auto-property cannot express.
#pragma warning disable IDE0032
    private IDictionary<string, string> _headers = new Dictionary<string, string>(StringComparer.Ordinal);
#pragma warning restore IDE0032

    public IDictionary<string, string> Headers
    {
        get => _headers;
        set => _headers = value ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "enableHsts",
            "Enable HSTS",
            FieldType.Boolean,
            new FieldEditableAlways(),
            RequiresRestart: true,
            HelpText: "Sends `Strict-Transport-Security` on every response, pinning the origin "
                + "to HTTPS in browsers that visit it. Off by default. "
                + "Browsers retain this pinning for `max-age` seconds regardless of subsequent "
                + "Collabhost changes -- including app rename, removal, or domain change. "
                + "Start with a short max-age (e.g. 300s = 5 minutes) to verify HSTS works on "
                + "your app before raising it. Production-ready values are typically 6 months "
                + "(15768000s) or 1-2 years. "
                + "Set HSTS Max Age to 0 to clear prior HSTS pinning for this origin (browser "
                + "un-pin signal)."
        ),
        new
        (
            "hstsMaxAgeSeconds",
            "HSTS Max Age (seconds)",
            FieldType.Number,
            new FieldEditableAlways(),
            RequiresRestart: true,
            HelpText: "How long browsers retain the HTTPS pin for this origin. Effective only "
                + "when Enable HSTS is on. Start small (300s = 5 minutes) to verify HSTS works "
                + "on your app; production-ready values are typically 15768000 (6 months) or "
                + "63072000 (2 years). Set to 0 to emit `max-age=0`, the browser un-pin signal "
                + "operators use to roll back a prior HSTS pin.",
            DependsOn: new FieldDependency("enableHsts", "true")
        ),
        new
        (
            "headers",
            "Response Headers",
            FieldType.KeyValue,
            new FieldEditableAlways(),
            RequiresRestart: true,
            HelpText: "Blanket response headers applied to every response from this app. "
                + "Key is the HTTP header name (e.g. `X-Content-Type-Options`, `Referrer-Policy`); "
                + "value is the header value (e.g. `nosniff`, `strict-origin-when-cross-origin`). "
                + "When the upstream emits the same header, Collabhost's value wins "
                + "(the proxy overwrites it, it does not merge). "
                + "To suppress a type-level default header on this app, set its value to empty "
                + "(the emitter drops empty-valued entries) -- this is the operator escape hatch "
                + "for the `X-Content-Type-Options: nosniff` platform default. "
                + "Do not set `Strict-Transport-Security` here while Enable HSTS is on -- choose "
                + "one channel.",
            KeyPattern: CapabilityResolver.SecurityHeaderKeyPatternString,
            KeyPatternMessage: CapabilityResolver.SecurityHeaderKeyPatternMessage
        ),
    ];
}
