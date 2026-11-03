using System.CommandLine;
using DcLedger.Application;
using DcLedger.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace DcLedger.Cli;

/// <summary>
/// <c>dcl archive-document</c> (issue #25): retrieves one public document, keeps its bytes in the private bucket under
/// their SHA-256, and prints the <c>data/documents/</c> record to paste into a pull request. For sources ingest doesn't
/// read, such as a decision's action-detail page or a news story. Exit 0 on success, 2 for bad arguments or
/// configuration, 3 when retrieval or the bucket failed (ADR-0004, ADR-0005).
/// </summary>
internal static class ArchiveDocumentCommand
{
    /// <summary>Creates the command. <paramref name="compose"/> builds the services for a real run (false) or a dry run (true).</summary>
    internal static Command Create(TextWriter output, TextWriter error, Func<bool, ServiceProvider> compose)
    {
        var url = new Option<string>("--url")
        {
            Description = "The public https URL of the document, for example a meeting's action-detail page.",
            Required = true,
        };
        var sourceType = new Option<string>("--source-type")
        {
            Description = "Who published it: official (a government body or its platform) or news.",
            Required = true,
        };
        sourceType.AcceptOnlyFromAmong(SourceType.Official.ToToken(), SourceType.News.ToToken());
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Retrieve and hash only; store nothing. Needs no credentials.",
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: text, or github for workflow annotations.",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong("text", "github");

        var command = new Command("archive-document", "Keep one document in the private bucket and print its ledger record.")
        {
            url,
            sourceType,
            dryRun,
            format,
        };
        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(url)!,
            parseResult.GetValue(sourceType) == SourceType.News.ToToken() ? SourceType.News : SourceType.Official,
            parseResult.GetValue(dryRun),
            parseResult.GetValue(format) == "github",
            output,
            error,
            compose,
            cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        string url,
        SourceType sourceType,
        bool dryRun,
        bool github,
        TextWriter output,
        TextWriter error,
        Func<bool, ServiceProvider> compose,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            error.WriteLine("--url must be an absolute https URL without a user name or password.");
            return ExitCodes.Usage;
        }

        ServiceProvider services;
        try
        {
            services = compose(dryRun);
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"Configuration: {exception.Message}");
            return ExitCodes.Usage;
        }

        await using (services.ConfigureAwait(false))
        {
            var result = await services.GetRequiredService<ArchiveDocument>()
                .RunAsync(new ArchiveRequest(uri, sourceType, dryRun), cancellationToken)
                .ConfigureAwait(false);
            if (result.Document is not { } document)
            {
                var message = result.Error ?? "The document wasn't archived.";
                if (github)
                {
                    output.WriteLine(WorkflowCommands.Error("archive-document", message));
                }
                else
                {
                    error.WriteLine($"Error: {message}");
                }

                return ExitCodes.ExternalFailure;
            }

            var stored = dryRun ? "not stored (dry run)" : result.Created ? "stored in the private bucket" : "already in the private bucket";
            var redirected = document.Url == uri ? string.Empty : $" (redirected from {uri.AbsoluteUri})";
            output.WriteLine($"Retrieved {document.Url.AbsoluteUri}{redirected}: {document.Bytes} bytes, {stored}.");
            output.WriteLine();
            output.WriteLine($"Add this file as {DocumentRecordYaml.PathFor(document.Id)}:");
            output.WriteLine();
            foreach (var line in DocumentRecordYaml.Lines(document.Id, document.Url, document.RetrievedAtUtc, document.MediaType, document.Bytes, document.SourceType))
            {
                output.WriteLine(line);
            }

            return ExitCodes.Ok;
        }
    }
}
