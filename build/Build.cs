using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.GitHub;
using Nuke.Common.Tools.NuGet;
using Octokit;
using Serilog;
using System.IO;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions("continuous",
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = false,
    EnableGitHubToken = true,
    On = new[] { GitHubActionsTrigger.Push },
    ImportSecrets = new[] { nameof(NewKey) },
    InvokedTargets = new[] { nameof(Release) })]
class Build : NukeBuild {
    public static int Main() => Execute<Build>(x => x.Release);

    [Parameter, Secret] private string NewKey;
    [Parameter, Secret] private string GITHUB_TOKEN;
    [Solution] readonly Solution Solution;

    AbsolutePath OutputDirectory => RootDirectory / "artifacts";

    Target Check => _ => _
        .OnlyWhenDynamic(() => IsServerBuild)
        .Executes(() => {
            Log.Information("is match: {value}", NewKey.Equals("hellonuke"));
        });

    Target Clean => _ => _
        .DependsOn(Check)
        .Executes(() => {
            OutputDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() => {
            DotNetRestore(s => s.SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() => {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration("Release")
                .EnableNoRestore());
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() => {
            DotNetPack(s => s
                .SetProject(Solution)
                .SetConfiguration("Release")
                .SetOutputDirectory(OutputDirectory)
                .EnableNoBuild());
        });

    Target Release => _ => _
        .DependsOn(Pack)
        .OnlyWhenDynamic(() => IsServerBuild)
        .Executes(async () => {
            var client = new GitHubClient(new ProductHeaderValue("nuke")) {
                Credentials = new Credentials(GITHUB_TOKEN)
            };

            var owner = "YangSpring429";
            var repo = "nuke_test";
            var version = "v1.0.0"; // 你可以换成自动版本号

            // 1. 创建 Release
            var newRelease = new NewRelease(version) {
                Name = version,
                Draft = false,
                Prerelease = false
            };

            var release = await client.Repository.Release.Create(owner, repo, newRelease);

            // 2. 上传 artifacts 目录下的所有文件
            foreach (var file in OutputDirectory.GlobFiles("*.nupkg")) {
                await client.Repository.Release.UploadAsset(
                    release,
                    new ReleaseAssetUpload {
                        FileName = file.Name,
                        ContentType = "application/octet-stream",
                        RawData = File.OpenRead(file)
                    });
            }
        });
}
