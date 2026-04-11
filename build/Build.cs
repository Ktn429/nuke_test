using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Serilog;
using System;
using System.Numerics;

[GitHubActions("continuous", 
    GitHubActionsImage.UbuntuLatest,
    On = new[] { GitHubActionsTrigger.Push },
    ImportSecrets = new[] { nameof(NewKey) },
    InvokedTargets = new[] { nameof(Check) })]
class Build : NukeBuild {
    public static int Main() => Execute<Build>(x => x.Check);

    [Parameter, Secret] private string NewKey;
    [Solution] readonly Solution Solution;

    AbsolutePath OutputDirectory => RootDirectory / "artifacts";

    Target Check => _ => _
        .OnlyWhenDynamic(() => IsServerBuild)
        .Executes(() => {
            Log.Information("is match: {value}", NewKey.Equals("hellonuke"));
        });

    //Target Clean => _ => _
    //    .Executes(() => {
    //        OutputDirectory.CreateOrCleanDirectory();
    //    });

    //Target Restore => _ => _
    //    .DependsOn(Clean)
    //    .Executes(() => {
    //        DotNetRestore(s => s.SetProjectFile(Solution));
    //    });

    //Target Compile => _ => _
    //    .DependsOn(Restore)
    //    .Executes(() => {
    //        DotNetBuild(s => s
    //            .SetProjectFile(Solution)
    //            .SetConfiguration("Release")
    //            .EnableNoRestore());
    //    });

    //Target Pack => _ => _
    //    .DependsOn(Compile)
    //    .Executes(() => {
    //        DotNetPack(s => s
    //            .SetProject(Solution)
    //            .SetConfiguration("Release")
    //            .SetOutputDirectory(OutputDirectory)
    //            .EnableNoBuild());
    //    });
}
