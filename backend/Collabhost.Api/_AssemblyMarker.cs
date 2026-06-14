namespace Collabhost.Api;

// Assembly-scope plumbing, not a subsystem type. Architecture arch-tests in
// Collabhost.Api.Tests anchor `typeof(IAssemblyMarker).Assembly` to walk the
// Api assembly's type graph without hard-coding an assembly name string. Owned
// by no single subsystem (it marks the whole assembly), so it lives in one named
// shared home at the Api root, alongside _GlobalUsings.cs.
internal interface IAssemblyMarker;
