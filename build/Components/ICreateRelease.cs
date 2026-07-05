using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tools.GitHub;
using Octokit;
using Serilog;

namespace Components;

public partial interface ICreateRelease : INukeBuild {
    public const string GitHubRelease = nameof(GitHubRelease);

    [GitRepository] [Required] GitRepository GitRepository => TryGetValue(() => GitRepository);
    [Parameter] [Secret] string GitHubToken => TryGetValue(() => GitHubToken) ?? GitHubActions.Instance?.Token;

    string Name { get; }
    string FileNameFormat { get; }
    
    bool Draft => false;

    IEnumerable<AbsolutePath> AssetFiles { get; }

    Target CreateGitHubRelease => _ => _
        .Requires(() => GitHubToken)
        .Executes(async () => {
            GitHubTasks.GitHubClient.Credentials = new Credentials(GitHubToken.NotNull());
            Log.Information("Starting create release...");
            
            var releaseName = await GetReleaseNameAsync();
            var release = await GetOrCreateReleaseAsync(releaseName);

            await Task.WhenAll(AssetFiles.Select(async file => {
                await using var stream = File.OpenRead(file);
        
                var fileName = string.Format(file.Name, releaseName);
        
                await GitHubTasks.GitHubClient.Repository.Release.UploadAsset(
                    release,
                    new ReleaseAssetUpload {
                        FileName = fileName,
                        ContentType = "application/octet-stream",
                        RawData = stream
                    });
        
                Log.Information("{Name} uploaded successfully!", fileName);
            }));

            return;
        
            async Task<string> GetReleaseNameAsync() {
                if (GitRepository.IsOnMainBranch())
                    return Name;
        
                var tags = await GitHubTasks.GitHubClient.Repository.GetAllTags(
                    GitRepository.GetGitHubOwner(),
                    GitRepository.GetGitHubName());
        
                var nextPreview = tags
                    .Where(x => x.Name.StartsWith($"{Name}-preview", StringComparison.OrdinalIgnoreCase))
                    .Select(x => {
                        var match = SuffixRegex().Match(x.Name);
                        return match.Success
                            ? int.Parse(match.Groups[1].Value)
                            : 0;
                    })
                    .DefaultIfEmpty()
                    .Max() + 1;
        
                return $"{Name}-preview{nextPreview}"; 
            }

            async Task<Release> GetOrCreateReleaseAsync(string releaseName) {
                try {
                    Log.Information("Creating {Name}...", releaseName);
        
                    return await GitHubTasks.GitHubClient.Repository.Release.Create(
                        GitRepository.GetGitHubOwner(),
                        GitRepository.GetGitHubName(),
                        new NewRelease(releaseName) {
                            Name = releaseName,
                            Draft = Draft,
                            Prerelease = !GitRepository.IsOnMainBranch(),
                            Body = ""
                        });
                }
                catch (ApiValidationException ex)
                    when (ex.ApiError?.Errors.Any(x =>
                        x.Code == "already_exists" &&
                        x.Field == "tag_name") == true)
                {
                    Log.Information("Release already exists, loading existing release...");
        
                    return await GitHubTasks.GitHubClient.Repository.Release.Get(
                        GitRepository.GetGitHubOwner(),
                        GitRepository.GetGitHubName(),
                        releaseName);
                }
            } 
        });
    
    [GeneratedRegex(@"preview(\d+)$")]
    private static partial Regex SuffixRegex();
}