# Contributing to MediatorLite

We welcome contributions! Here's how to get involved.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR_USERNAME/MediatorLite.git`
3. Create a branch: `git checkout -b feature/your-feature`

## Building

```bash
cd MediatorLite
dotnet build
```

## Running Tests

```bash
dotnet test
```

## Running Benchmarks

```bash
cd tests/MediatorLite.Benchmarks
dotnet run -c Release
```

## Coding Guidelines

- Follow existing code style
- Add XML documentation to public APIs
- Include unit tests for new features
- Keep commits focused and atomic

## Pull Request Process

1. Update documentation as needed
2. Add tests for new functionality
3. Ensure all tests pass
4. Update CHANGELOG.md
5. Submit PR with clear description

## Package Versioning and Release Guidelines

Use these rules to keep package restores predictable for new users.

### Package roles

- `MediatorLite.Abstractions`: contracts only
- `MediatorLite`: runtime implementation, depends on `MediatorLite.Abstractions`
- `MediatorLite.SourceGeneration`: analyzer/source generator package

### Compatibility policy

- Support same major/minor across all packages.
- Treat cross-major combinations as unsupported.
- Keep package versions lockstep when possible.

### Version bump rules

- Patch:
	- bug fixes, docs, internal changes
	- no public contract break in `MediatorLite.Abstractions`
- Minor:
	- additive, backward-compatible API changes
- Major:
	- breaking API or behavior changes

### Release checklist

1. Update versions for all affected projects.
2. Run `dotnet restore`, `dotnet build -c Release`, and `dotnet test`.
3. Pack projects and inspect generated nuspec files.
4. Verify the generated `MediatorLite` nuspec (created under `obj/Release` after `dotnet pack`) contains dependency on `MediatorLite.Abstractions`.
5. Update README compatibility matrix if support policy changes.
6. Publish all packages in one release train.

## Reporting Issues

- Use GitHub Issues
- Include reproduction steps
- Mention .NET version and OS
- Include stack traces if applicable

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
