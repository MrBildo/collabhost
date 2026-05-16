using Collabhost.Api.Probes;

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
//
// The provision gate is for the single-file self-extraction case ONLY, not
// every hosted dotnet-app. A self-contained single-file publish must unpack
// its embedded native libraries to a writable dir on cold start; a
// framework-dependent / non-single-file publish (loose *.dll beside a root
// *.runtimeconfig.json) does no extraction at all and gains nothing from the
// variable. The original two-clause gate (dotnet-app && !operatorPinned)
// over-provisioned: it fabricated an unused bundle dir + injected an inert env
// var for non-self-extracting apps -- which is exactly what it did in the only
// production install (a non-single-file v1.12.1 app). The selfExtracts clause
// is the discriminator; it keys off the same ArtifactEvidenceCollector
// single-file evidence the codebase already uses for probe extraction. (#322
// decision 3 -- narrow the gate.)
internal static class HostedDotnetBundleEnvironment
{
    public const string BundleExtractBaseDirVariable = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";

    // The literal app-type slug; the codebase compares this string directly with
    // StringComparison.Ordinal elsewhere (e.g. ProbeCurator) rather than via a
    // shared constant -- matched here for consistency.
    public const string DotnetAppTypeSlug = "dotnet-app";

    // True when the supervisor should provision a per-app extraction dir for this
    // app. Gated to:
    //   - app type dotnet-app, AND
    //   - the artifact actually self-extracts (single-file publish), AND
    //   - the operator has NOT pinned the variable.
    //
    // operatorPinned reports the pin independently so the caller can reason
    // about the escape-hatch path if needed. The operator-override gate keys on
    // operatorOverrideKeys, not on the merged value: the dotnet-app
    // type-defaults JSON does not carry this key, so the only way it is present
    // in the resolved variables is an operator override.
    //
    // selfExtracts is computed by the caller from the on-disk artifact (see
    // ArtifactSelfExtracts) so this stays a pure predicate -- the established
    // shape for the supervisor's start-time gates.
    public static bool ShouldProvision
    (
        string appTypeSlug,
        FrozenSet<string> operatorOverrideKeys,
        bool selfExtracts,
        out bool operatorPinned
    )
    {
        operatorPinned = operatorOverrideKeys.Contains(BundleExtractBaseDirVariable);

        return string.Equals(appTypeSlug, DotnetAppTypeSlug, StringComparison.Ordinal)
            && selfExtracts
            && !operatorPinned;
    }

    // The self-extraction discriminator. A self-contained single-file publish
    // has NO root *.runtimeconfig.json and NO *.csproj -- so
    // ArtifactEvidenceCollector falls through to its single-file binary
    // detection and emits a SingleFileBinary signal. A framework-dependent /
    // non-single-file publish emits a RuntimeConfig signal instead and produces
    // no SingleFileBinary. That difference is exactly "does this artifact
    // self-extract on cold start", and it is the same evidence DotnetExtractor
    // already keys off for single-file probe extraction.
    //
    // I/O-bearing (a directory scan); kept separate from the pure ShouldProvision
    // predicate. The caller invokes it only after the cheap app-type gate so a
    // non-dotnet app never triggers the scan.
    public static bool ArtifactSelfExtracts(string artifactLocation)
    {
        if (string.IsNullOrWhiteSpace(artifactLocation))
        {
            return false;
        }

        var evidence = ArtifactEvidenceCollector.Collect(artifactLocation, DotnetAppTypeSlug);

        foreach (var signal in evidence.Signals)
        {
            if (string.Equals(signal.Kind, EvidenceSignalKinds.SingleFileBinary, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
