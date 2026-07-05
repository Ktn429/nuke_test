using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tools.GitHub;
using Octokit;
using Serilog;

namespace Components;

public interface ICreateRelease : INukeBuild {
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
            
            var suffix = string.Empty;
            var release = await GetOrCreateRelease();
            
            var uploadTasks = AssetFiles.Select(async x => {
                await using var assetFile = File.OpenRead(x);
                var asset = new ReleaseAssetUpload { 
                    FileName = string.Format(x.Name, $"{Name}{suffix}"), 
                    ContentType = "application/octet-stream", 
                    RawData = assetFile
                };
                await GitHubTasks.GitHubClient.Repository.Release.UploadAsset(release, asset);
                Log.Information("{Name} uploaded successfully!", x.Name);
            }).ToArray();
            
            Task.WaitAll(uploadTasks);
            return;

            async Task<Release> GetOrCreateRelease() {
                try {
                    if (!GitRepository.IsOnMainBranch()) {
                        var allTags = await GitHubTasks.GitHubClient.Repository.GetAllTags(
                            GitRepository.GetGitHubOwner(),
                            GitRepository.GetGitHubName());

                        var firstPreTag = allTags
                            .FirstOrDefault(tag => tag.Name.Contains("preview", StringComparison.OrdinalIgnoreCase))?
                            .Name
                            .Split("preview");

                        var newPreTagNumber = Convert.ToInt32(firstPreTag?[1]) + 1;
                        suffix = firstPreTag![0].Equals(Name, StringComparison.OrdinalIgnoreCase)
                            ? $"-preview{newPreTagNumber}"
                            : "-preview1";
                    }

                    var name = $"{Name}{suffix}";
                    Log.Information("Creating {Name}...", name);
                    return await GitHubTasks.GitHubClient.Repository.Release.Create(
                        GitRepository.GetGitHubOwner(),
                        GitRepository.GetGitHubName(),
                        new NewRelease(name) {
                            Name = name,
                            Prerelease = !GitRepository.IsOnMainBranch(),
                            Draft = Draft,
                            Body = ""
                        });
                }
                catch(Exception ex) {
                    Log.Error(ex, "Failed to create release!");
                }
                catch {
                    return await GitHubTasks.GitHubClient.Repository.Release.Get(
                        GitRepository.GetGitHubOwner(),
                        GitRepository.GetGitHubName(),
                        Name);
                }
            }
        });
}