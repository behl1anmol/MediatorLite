; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 2.0.5

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MEDL1001 | MediatorLite.Validation | Error | FluentValidation validators were found but the MediatorLite.FluentValidation package is not referenced.
MEDL1002 | MediatorLite.Behaviors | Warning | An open generic pipeline behavior does not match the supported Behavior&lt;TRequest, TResponse&gt; shape and was not registered.

## Release 2.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MEDL1003 | MediatorLite.Handlers | Warning | Multiple handlers are registered for the same (request, response) pair; only the last registration is invoked at dispatch time.
MEDL1004 | MediatorLite.Handlers | Warning | A handler class is generic or nested inside a generic type and cannot be registered by the source generator.
MEDL1005 | MediatorLite.Behaviors | Warning | An open generic pipeline behavior expanded to zero registrations (a class-constrained open behavior when every discovered request is a value type) and was not registered.
