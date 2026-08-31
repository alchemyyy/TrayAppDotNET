namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Identifies how Windows launches one startup application.</summary>
internal enum StartupAppSourceKind : byte
{
    RegistryRun,
    RegistryRunOnce,
    StartupFolder
}

/// <summary>Identifies whether a startup application applies to one user or the machine.</summary>
internal enum StartupAppScope : byte
{
    CurrentUser,
    AllUsers
}

/// <summary>Identifies the registry view containing a startup registration.</summary>
internal enum StartupAppRegistryView : byte
{
    None,
    Registry32,
    Registry64
}

/// <summary>Represents the state encoded in an Explorer StartupApproved value.</summary>
internal enum StartupAppStatus : byte
{
    Unknown,
    Enabled,
    Disabled
}

/// <summary>Represents the startup impact shown before Windows has measured an application.</summary>
internal enum StartupAppImpact : byte
{
    NotMeasured,
    None,
    Low,
    Medium,
    High
}

/// <summary>Stable identity for one startup registration.</summary>
internal readonly record struct StartupAppIdentity(
    StartupAppSourceKind SourceKind,
    StartupAppScope Scope,
    StartupAppRegistryView RegistryView,
    string SourceLocation,
    string EntryName);

/// <summary>Identifies the StartupApproved value that controls one startup registration.</summary>
internal readonly record struct StartupAppApprovalTarget(
    StartupAppScope Scope,
    StartupAppRegistryView RegistryView,
    string RegistrySubKey,
    string ValueName)
{
    public bool IsValid =>
        RegistryView != StartupAppRegistryView.None
        && !string.IsNullOrWhiteSpace(RegistrySubKey)
        && ValueName != null;
}

/// <summary>Describes which Startup Apps commands apply to the current row.</summary>
internal readonly record struct StartupAppActionEligibility(
    bool CanEnable,
    bool CanDisable,
    bool CanShowProperties)
{
    /// <summary>Creates command eligibility from status and target availability.</summary>
    public static StartupAppActionEligibility Create(
        StartupAppStatus status,
        bool supportsStatusChange,
        bool hasResolvedTarget) =>
        status switch
        {
            StartupAppStatus.Enabled => new StartupAppActionEligibility(
                CanEnable: false,
                CanDisable: supportsStatusChange,
                CanShowProperties: hasResolvedTarget),
            StartupAppStatus.Disabled => new StartupAppActionEligibility(
                CanEnable: supportsStatusChange,
                CanDisable: false,
                CanShowProperties: hasResolvedTarget),
            _ => new StartupAppActionEligibility(
                CanEnable: false,
                CanDisable: false,
                CanShowProperties: hasResolvedTarget)
        };
}

/// <summary>One normalized startup registration displayed by the Startup Apps page.</summary>
internal sealed record StartupAppEntry(
    StartupAppIdentity Identity,
    string Name,
    string Publisher,
    StartupAppStatus Status,
    StartupAppImpact Impact,
    string Command,
    string? TargetPath,
    string? ExecutablePath,
    StartupAppApprovalTarget ApprovalTarget)
{
    /// <summary>Gets commands that apply to the current registration state.</summary>
    public StartupAppActionEligibility ActionEligibility => StartupAppActionEligibility.Create(
        Status,
        ApprovalTarget.IsValid,
        !string.IsNullOrWhiteSpace(ExecutablePath));
}

/// <summary>Reports the outcome of changing one StartupApproved value.</summary>
internal readonly record struct StartupAppActionResult(
    bool Succeeded,
    StartupAppStatus Status,
    string ErrorMessage)
{
    public static StartupAppActionResult Success(StartupAppStatus status) =>
        new(true, status, string.Empty);

    public static StartupAppActionResult Failure(
        StartupAppStatus status,
        string errorMessage) =>
        new(false, status, errorMessage);
}
