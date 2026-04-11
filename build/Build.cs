using Nuke.Common;
using Nuke.Common.ChangeLog;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.GitHub;
using Nuke.Common.Tools.GitReleaseManager;
using Nuke.Common.Tools.NuGet;
using Octokit;
using Serilog;
using System;
using System.IO;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.GitReleaseManager.GitReleaseManagerTasks;

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

    private AbsolutePath OutputDirectory => RootDirectory / "artifacts";

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

            var owner = "YangSpring429";
            var repo = "nuke_test";
            var version = $"{Solution.GetProject("nuke_test").GetProperty("Version")}";

            // 1. 创建草稿 Release（自动生成 Release Notes）
            GitReleaseManagerCreate(s => s
                .SetRepositoryOwner(owner)
                .SetRepositoryName(repo)
                .SetName(version)
                .SetToken(GITHUB_TOKEN)
                .SetPrerelease(version.Contains("pre", StringComparison.InvariantCultureIgnoreCase))
            );

            // 2. 上传构建产物
            GitReleaseManagerAddAssets(s => s
                .SetRepositoryOwner(owner)
                .SetRepositoryName(repo)
                .SetTagName(version)
                .SetAssetPaths(OutputDirectory / "*.nupkg")
                .SetToken(GITHUB_TOKEN)
            );

            // 3. 发布 Release
            GitReleaseManagerPublish(s => s
                .SetRepositoryOwner(owner)
                .SetRepositoryName(repo)
                .SetTagName(version)
                .SetToken(GITHUB_TOKEN)
            );

            // 4. 关闭 milestone（可选）
            GitReleaseManagerClose(s => s
                .SetRepositoryOwner(owner)
                .SetRepositoryName(repo)
                .SetToken(GITHUB_TOKEN)
            );
        });
}
