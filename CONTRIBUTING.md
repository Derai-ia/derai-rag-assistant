# Contributing

1. Install the .NET 10 SDK and restore the solution with `dotnet restore Derai.RagAssistant.slnx`.
2. Keep changes within the existing Domain, Application, Infrastructure, and API boundaries. Do not commit credentials, `.env` files, build output, or generated test results.
3. Before opening a pull request, run:

   ```bash
   dotnet format Derai.RagAssistant.slnx --verify-no-changes --no-restore
   dotnet build Derai.RagAssistant.slnx -c Release --no-restore -warnaserror -m:1 -nodeReuse:false
   dotnet test Derai.RagAssistant.slnx -c Release --no-build -m:1 -nodeReuse:false --blame-hang --blame-hang-timeout 60s --blame-hang-dump-type none
   ```

4. Add or update tests for behavior changes, especially role ACL and both corpus languages. Keep corpus examples fictional and aligned across translations.
5. Use a concise conventional commit subject when committing in your fork.
