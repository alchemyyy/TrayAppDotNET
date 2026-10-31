using Xunit;

namespace TrayAppDotNETCommon.TestSupport;

/// <summary>
/// A fact that only runs when an opt-in environment variable is set to <c>1</c>. Without it the test reports as
/// skipped with the variable name, rather than passing without running.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EnvironmentFactAttribute : FactAttribute
{
    public EnvironmentFactAttribute(string environmentVariable)
    {
        EnvironmentVariable = environmentVariable;
        if (!string.Equals(Environment.GetEnvironmentVariable(environmentVariable), b: "1", StringComparison.Ordinal))
            Skip = $"Set {environmentVariable}=1 to run this test.";
    }

    public string EnvironmentVariable { get; }
}
