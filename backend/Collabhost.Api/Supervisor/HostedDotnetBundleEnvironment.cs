namespace Collabhost.Api.Supervisor;

// Decides whether a hosted app should receive a platform-provisioned
// DOTNET_BUNDLE_EXTRACT_BASE_DIR. When it should, the supervisor injects the
// value into the resolved capability-variables dictionary -- the SAME tier as
// the operator's environment-defaults override (resolved type-default +
// override), BEFORE the IProcessEnvironmentProvider/port-injection tiers.
//
// Why not an IProcessEnvironmentProvider: the provider tier *wins over* the
// capability/operator-override tier (it only emits a shadow warning -- see
// MergeEnvironmentVariables). That semantic is correct for a secret source of
// truth (the DNS API token) but wrong for a *default*: an operator who
// deliberately pins DOTNET_BUNDLE_EXTRACT_BASE_DIR must win silently. Injecting
// into the capability-variables tier, gated on the operator not having already
// set the key, is what preserves that escape hatch. (#313 / CH-C.)
internal static class HostedDotnetBundleEnvironment
{
    public const string BundleExtractBaseDirVariable = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";

    // The literal app-type slug; the codebase compares this string directly with
    // StringComparison.Ordinal elsewhere (e.g. ProbeCurator) rather than via a
    // shared constant -- matched here for consistency.
    public const string DotnetAppTypeSlug = "dotnet-app";

    // True when the supervisor should provision a per-app extraction dir for this
    // app; false when the type is not dotnet-app or the operator already pinned
    // the variable. operatorPinned reports the pin independently so the caller
    // can reason about the escape-hatch path if needed.
    //
    // The operator-override gate keys on operatorOverrideKeys, not on the merged
    // value: the dotnet-app type-defaults JSON does not carry this key, so the
    // only way it is present in the resolved variables is an operator override.
    public static bool ShouldProvision
    (
        string appTypeSlug,
        FrozenSet<string> operatorOverrideKeys,
        out bool operatorPinned
    )
    {
        operatorPinned = operatorOverrideKeys.Contains(BundleExtractBaseDirVariable);

        return string.Equals(appTypeSlug, DotnetAppTypeSlug, StringComparison.Ordinal) && !operatorPinned;
    }
}
