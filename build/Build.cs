using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.NuGet;
using Nuke.Components;
using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions("continuous",
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = false,
    PublishArtifacts = true,
    EnableGitHubToken = true,
    On = new[] { GitHubActionsTrigger.Push },
    ImportSecrets = new[] { nameof(NewKey) },
    InvokedTargets = new[] { nameof(Release) })]
class Build : NukeBuild, IHazSolution, IPack, ICompile, IRestore, ICreateGitHubRelease {
    [Parameter, Secret] private string NewKey;

    [Solution(GenerateProjects = true)] readonly Solution Solution;
    Nuke.Common.ProjectModel.Solution IHazSolution.Solution => Solution;

    IEnumerable<AbsolutePath> NuGetPackageFiles
            => From<IPack>().PackagesDirectory.GlobFiles("*.nupkg");

    private AbsolutePath OutputDirectory => RootDirectory / "artifacts";

    string ICreateGitHubRelease.Name => Solution.nuke_test.GetProperty("Version");
    bool ICreateGitHubRelease.Prerelease => Solution.nuke_test.GetProperty("Version").Contains("pre", StringComparison.OrdinalIgnoreCase);

    IEnumerable<AbsolutePath> ICreateGitHubRelease.AssetFiles => NuGetPackageFiles;

    public static int Main() => Execute<Build>(x => ((IPack)x).Pack);

    private T From<T>() where T : INukeBuild
        => (T)(object)this;

    public Target Check => _ => _
       .OnlyWhenDynamic(() => IsServerBuild)
       .Executes(() => {
           Log.Information("is match: {value}", NewKey.Equals("hellonuke"));
       });

    Target Clean => _ => _
        .DependsOn(Check)
        .Executes(() => {
            OutputDirectory.CreateOrCleanDirectory();
            Log.Information("clear over!");
        });

    Target IRestore.Restore => _ => _
        .DependsOn(Clean)
        .Executes(() => {
            DotNetRestore(s => s.SetProjectFile(Solution));
        });

    Target ICompile.Compile => _ => _
        .TryDependsOn<IRestore>()
        .Executes(() => {
            DotNetPublish(s => s
                .SetProject(Solution.nuke_test)
                .SetConfiguration(Configuration.Release)
                .SetOutput(OutputDirectory)
                .EnableNoRestore());
        });

    Target IPack.Pack => _ => _
        .TryDependsOn<ICompile>()
        .TryTriggers<ICreateGitHubRelease>()
        .Executes(() => {
            DotNetPack(x => x
                .SetProject(Solution.nuke_test)
                .SetConfiguration(Configuration.Release)
                .SetOutputDirectory(OutputDirectory)
                .EnableNoBuild());
        });

    Target ICreateGitHubRelease.CreateGitHubRelease => _ => _
        .Inherit<ICreateGitHubRelease>()
        .OnlyWhenStatic(() => IsServerBuild);
}