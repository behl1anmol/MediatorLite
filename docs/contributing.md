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

## Reporting Issues

- Use GitHub Issues
- Include reproduction steps
- Mention .NET version and OS
- Include stack traces if applicable

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
