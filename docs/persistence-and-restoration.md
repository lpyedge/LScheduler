# Persistence and Restart Restoration

Persistent jobs are restored automatically after restart.

`LScheduler` reloads the saved job definition from the configured store and rebuilds the executable action by:

- resolving the saved method type
- locating the saved public method
- creating the target instance
  - through DI when a root `IServiceProvider` is available
  - through `Activator.CreateInstance(...)` otherwise
- deserializing the saved payload JSON when needed

There is no separate handler registration API.

## What Persistent Jobs Require

Persistent jobs must use a direct method group on a stable named type.

Supported:

- public static methods
- public instance methods on DI-resolvable services
- public instance methods on constructible types

Not supported for persistent jobs:

- anonymous lambdas
- closures
- local functions
- compiler-generated delegates

Those should use `AddInMemoryJobAsync(...)`.

## Restoration Semantics

When a persistent job already exists in the store:

- the saved metadata is loaded first
- `LastRunTimeUtc` is restored
- `NextRunTimeUtc` is reused when the schedule has not changed
- `NextRunTimeUtc` is recalculated when the schedule changes

One-time persistent jobs are removed from the store after a successful run.

If a one-time persistent job is cancelled during shutdown before it completes, the persisted job remains in the store and can be restored on the next start.
