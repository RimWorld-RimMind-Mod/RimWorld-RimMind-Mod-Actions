# RimMind Actions tests

Unit and contract tests for `RimMind-Actions` covering all 11 composite tools,
`CompositeToolCallBase` execution guards, `CompositeToolRegistrar`, `ActionsToolArgumentAdapter`,
`ResourceSafetyPolicy`, `BuildableStructureCatalogPolicy`, and `ColonySpatialClusterSolver`.

Tests run with `dotnet test RimMind-Actions/Tests/RimMindActions.Tests.csproj -c Release`
under `C# 9.0` compatibility (`RIMMIND_ACTIONS_TESTS`), staying well below the `< 1000`
per-module test-case budget.

Deletion of any test file requires explicit owner approval for that exact path;
directories are never deletion candidates.
