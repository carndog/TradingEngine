# C# Coding Standards

These standards apply to all C# production and test code in this repository.

## Non-negotiable rules

- Declare an explicit type for every local variable and `foreach` iteration variable. Never use `var`.
- Do not add `//`, `/* */` or XML documentation comments to C# code. Express intent through clear naming and extracted methods.
- Prefer private methods over local functions.
- Keep one class, interface, record or enum per file.
- Separate interface method declarations with a blank line.
- Make the smallest change that satisfies the requirement.
- Keep formatting consistent with surrounding code and favour readability over cleverness.
- Write boolean negation as `is false` rather than the `!` operator.

## Tests

- Use NUnit `[Test]`, `[TestCase]`, `[SetUp]`, `[TearDown]` and `[TestFixture]` attributes.
- Name test methods `MethodUnderTest_Condition_ExpectedResult`.
- Structure tests as Arrange, Act and Assert without `Arrange`, `Act` or `Assert` comments.
- Use NUnit `Assert.That` assertions.
- Do not supply description or reason strings to assertions.
- Use helpers or builders when test setup becomes difficult to read.
- Keep tests deterministic. Use fixed values and injected clock abstractions rather than the system clock.

## Completion checks

Run the following commands from the repository root:

```powershell
dotnet build TradingEngine.sln
dotnet test TradingEngine.sln
```

Review the complete diff before committing. Confirm that it follows these standards, preserves the architecture rules and complies with `PUBLIC_SCOPE.md`.
