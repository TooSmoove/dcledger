using DcLedger.Application;

namespace DcLedger.Cli;

/// <summary>Prints quality-gate problems the same way for <c>dcl validate</c> and <c>dcl export</c>.</summary>
internal static class LedgerProblems
{
    /// <summary>One line per problem: <c>file: [RULE] record: message</c>, or a workflow annotation when <paramref name="github"/>.</summary>
    public static void Print(IReadOnlyList<ValidationError> problems, string dataDirectory, bool github, TextWriter output)
    {
        foreach (var problem in problems)
        {
            var file = Path.Join(dataDirectory, problem.File).Replace('\\', '/');
            var record = problem.RecordId is null ? string.Empty : $"{problem.RecordId}: ";
            output.WriteLine(github
                ? WorkflowCommands.FileError(file, problem.Rule, record + problem.Message)
                : $"{file}: [{problem.Rule}] {record}{problem.Message}");
        }
    }
}
