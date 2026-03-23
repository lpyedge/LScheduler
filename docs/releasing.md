# Releasing

The repository includes `.github/workflows/publish.yml`.

It restores, builds, tests, packs, and publishes:

- `LScheduler`
- `LScheduler.Sqlite`
- `LScheduler.MySql`
- `LScheduler.SqlServer`
- `LScheduler.Redis`

Triggers:

- GitHub release published
- manual `workflow_dispatch`

## Release vs Manual Dispatch

When the workflow is triggered by a published GitHub release:

- all five packages are packed
- all five packages are pushed to NuGet

When the workflow is triggered manually through `workflow_dispatch`:

- package publishing is controlled by five boolean inputs
- only the selected packages are packed and pushed
- the workflow fails fast if nothing is selected

## Manual Dispatch Inputs

Preview versioning:

- `preview`
- `version_suffix`

Package selection:

- `publish_lscheduler`
- `publish_sqlite`
- `publish_mysql`
- `publish_sqlserver`
- `publish_redis`

## Typical Usage

Publish every package as preview:

- keep all five `publish_*` options checked
- set `preview = true`
- keep `version_suffix = preview` or set a custom suffix

Publish only one provider package:

- uncheck the packages you do not want to publish
- leave only the target package checked
- optionally disable preview by setting `preview = false`

Publish the core package without providers:

- check `publish_lscheduler`
- uncheck `publish_sqlite`
- uncheck `publish_mysql`
- uncheck `publish_sqlserver`
- uncheck `publish_redis`

NuGet publishing uses the `NUGET_API_KEY` repository secret.
