; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MEDL1001 | MediatorLite.Validation | Error | FluentValidation validators were found but the MediatorLite.FluentValidation package is not referenced.
MEDL1002 | MediatorLite.Behaviors | Warning | An open generic pipeline behavior does not match the supported Behavior<TRequest, TResponse> shape and was not registered.
MEDL1003 | MediatorLite.Handlers | Warning | Multiple handlers are registered for the same (request, response) pair; only the last registration is invoked at dispatch time.
MEDL1004 | MediatorLite.Handlers | Warning | A handler class is generic or nested inside a generic type and cannot be registered by the source generator.
