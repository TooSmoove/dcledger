using DcLedger.Cli;

return await CliApp.RunAsync(args, Console.Out, Console.Error, CancellationToken.None).ConfigureAwait(false);
