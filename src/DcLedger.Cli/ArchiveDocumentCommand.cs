using System.CommandLine;
using System.Globalization;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Infrastructure.Fetching;
using Microsoft.Extensions.DependencyInjection;

namespace DcLedger.Cli;

/// <summary>
/// <c>dcl archive-document</c> (issue #25): retrieves one public document, keeps its bytes in the private bucket under
/// their SHA-256, and prints the <c>data/documents/</c> record to paste into a pull request. For sources ingest doesn't
/// read, such as a decision's action-detail page or a news story. With <c>--file</c> (issue #101) it keeps a copy a person
/// saved in a browser instead of fetching, for sources that serve only browsers. Exit 0 on success, 2 for bad arguments,
/// an unusable file or configuration, 3 when retrieval or the bucket failed (ADR-0004, ADR-0005).
/// </summary>
internal static class ArchiveDocumentCommand
{
    // A file's clock and ours are the same computer's, so anything beyond a little rounding is a wrong clock or a typo.
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(1);

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
        var file = new Option<FileInfo?>("--file")
        {
            Description = "A copy of --url you saved in a browser, for sources that block programs. Nothing is fetched; "
                + "the record says retrieval: manual and uses the file's last-modified time as the retrieval time.",
        };
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
            file,
            dryRun,
            format,
        };
        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(url)!,
            parseResult.GetValue(sourceType) == SourceType.News.ToToken() ? SourceType.News : SourceType.Official,
            parseResult.GetValue(file),
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
        FileInfo? file,
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

        SavedCopy? copy = null;
        if (file is not null)
        {
            var (saved, problem) = await ReadSavedCopyAsync(file, cancellationToken).ConfigureAwait(false);
            if (saved is null)
            {
                error.WriteLine(problem);
                return ExitCodes.Usage;
            }

            copy = saved;
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
                .RunAsync(new ArchiveRequest(uri, sourceType, dryRun, copy), cancellationToken)
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
            if (copy is null)
            {
                var redirected = document.Url == uri ? string.Empty : $" (redirected from {uri.AbsoluteUri})";
                output.WriteLine($"Retrieved {document.Url.AbsoluteUri}{redirected}: {document.Bytes} bytes, {stored}.");
            }
            else
            {
                output.WriteLine($"Read {copy.FileName}, saved from {document.Url.AbsoluteUri}: {document.Bytes} bytes, {stored}.");
                output.WriteLine($"retrieved_at is the file's last-modified time, {Utc(document.RetrievedAtUtc)}; check that's when you saved it.");
            }

            output.WriteLine();
            output.WriteLine($"Add this file as {DocumentRecordYaml.PathFor(document.Id)}:");
            output.WriteLine();
            foreach (var line in DocumentRecordYaml.Lines(document))
            {
                output.WriteLine(line);
            }

            return ExitCodes.Ok;
        }
    }

    /// <summary>Reads a browser-saved copy, or says why it can't be used: missing, empty, too big or saved in the future.</summary>
    private static async Task<(SavedCopy? Copy, string Problem)> ReadSavedCopyAsync(FileInfo file, CancellationToken cancellationToken)
    {
        file.Refresh();
        if (!file.Exists)
        {
            return (null, $"--file: {file.FullName} doesn't exist.");
        }

        if (file.Length == 0)
        {
            return (null, $"--file: {file.Name} is empty.");
        }

        var maxBytes = new FetcherOptions().MaxBytes;
        if (file.Length > maxBytes)
        {
            return (null, $"--file: {file.Name} is {file.Length} bytes; the limit is {maxBytes}, the same as for downloads.");
        }

        var savedAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        if (savedAt > DateTimeOffset.UtcNow + FutureTolerance)
        {
            return (null, $"--file: {file.Name} was last modified in the future ({Utc(savedAt)}); check the computer's clock.");
        }

        try
        {
            var content = await File.ReadAllBytesAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            return (new SavedCopy(content, file.Name, savedAt), string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, $"--file: {file.Name} couldn't be read: {exception.Message}");
        }
    }

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
