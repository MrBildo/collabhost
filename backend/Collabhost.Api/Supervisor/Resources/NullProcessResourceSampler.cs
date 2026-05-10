namespace Collabhost.Api.Supervisor.Resources;

// Used on platforms without a tuned sampler (i.e. anything that is not Windows or
// Linux). Returns null for every Sample call so the AppDetail.resources field stays
// null on unsupported hosts -- the same shape as "process not running."
public class NullProcessResourceSampler : IProcessResourceSampler
{
    public ProcessResourceSnapshot? Sample(int pid) => null;

    public void Forget(int pid)
    {
        // No state to forget.
    }
}
