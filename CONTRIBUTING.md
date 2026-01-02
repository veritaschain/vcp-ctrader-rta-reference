# Contributing to VCP cTrader RTA Reference

Thank you for your interest in contributing to the VCP cTrader RTA Reference Implementation!

## How to Contribute

### Reporting Issues

- Use the GitHub issue tracker to report bugs
- Provide detailed steps to reproduce the issue
- Include relevant logs, error messages, and system information
- Check existing issues before creating a new one

### Pull Requests

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Run tests and ensure code quality
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

### Code Style

- Follow C# coding conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods focused and concise

### Commit Messages

- Use clear, descriptive commit messages
- Start with a verb (Add, Fix, Update, Remove, etc.)
- Reference issues when applicable (e.g., "Fix #123: Handle null reference")

### Documentation

- Update README.md if adding new features
- Add XML documentation for new public APIs
- Update docs/ for significant changes

## Development Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/veritaschain/vcp-ctrader-rta-reference.git
   ```

2. Open in Visual Studio or VS Code:
   ```bash
   cd vcp-ctrader-rta-reference
   code .
   ```

3. Build the solution:
   ```bash
   dotnet build
   ```

## Testing

- Write unit tests for new functionality
- Ensure existing tests pass before submitting PR
- Test with actual cTrader environment when possible

## VCP Specification Compliance

When making changes, ensure compliance with:
- VCP Specification v1.1
- RFC 6962 (Merkle tree construction)
- RFC 9562 (UUID v7)
- RFC 8785 (JSON Canonicalization)

## License

By contributing, you agree that your contributions will be licensed under CC BY 4.0.

## Questions?

Feel free to open an issue for questions or discussions about potential contributions.
