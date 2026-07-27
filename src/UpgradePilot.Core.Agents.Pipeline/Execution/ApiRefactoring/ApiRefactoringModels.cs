namespace UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

public sealed record RenameRule(string OldName, string NewName);

public sealed record ApiRefactoringInput(string SourcePath, IReadOnlyList<RenameRule> Renames);

public sealed record RefactoringChange(string OldName, string NewName, int OccurrencesReplaced);

public sealed record RefactoringReport(string RefactoredSource, IReadOnlyList<RefactoringChange> Changes);
