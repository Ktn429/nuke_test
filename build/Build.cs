using System.IO.Abstractions;
using System.Threading.Tasks;
using DotnetPackaging.AppImage;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Serilog;
using Zafiro.DivineBytes;
using Zafiro.DivineBytes.System.IO;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions("continuous",
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    PublishArtifacts = true,
    EnableGitHubToken = true,
    On = [GitHubActionsTrigger.Push],
    InvokedTargets = [nameof(Finish)])]
public class Build : NukeBuild {
    [Solution(GenerateProjects = true)] Solution Solution;
    
    private readonly AbsolutePath OutputDirectory = RootDirectory / "artifacts";
    private readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    public static int Main() => Execute<Build>(x => x.Finish);

    Target Clean => _ => _
        .Executes(() => {
            OutputDirectory.CreateOrCleanDirectory();
            Log.Information("Clean up");
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() => {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });
    
     Target PublishWindows => _ => _
         .DependsOn(Restore)
         .Executes(() => Publish(DotNetRuntimeIdentifier.win_x64), () => Publish(DotNetRuntimeIdentifier.win_x86), () => Publish(DotNetRuntimeIdentifier.win_arm64));
     
     Target PublishMacOS => _ => _
         .DependsOn(Restore)
         .Executes(() => Publish(DotNetRuntimeIdentifier.osx_x64), () => Publish("osx-arm64"));
     
     Target PublishLinux => _ => _
         .DependsOn(Restore)
         .Executes(() => Publish(DotNetRuntimeIdentifier.linux_arm64), () => Publish(DotNetRuntimeIdentifier.linux_arm));

     Target InstallTool => _ => _
         .DependsOn(PublishLinux)
         .OnlyWhenStatic(() => IsServerBuild)
         .Executes(() => {
             DotNetToolInstall(x => x
                 .SetPackageName("KuiperZone.PupNet")
                 .EnableGlobal());

             ProcessTasks.StartShell("sudo apt install libfuse2 -y")
                 .AssertWaitForExit();
         });

     Target PackLinux => _ => _
         .DependsOn(InstallTool)
         .Executes(() =>
         {
             
         });

     Target Finish => _ => _ 
         .DependsOn(PublishWindows, PublishMacOS, PackLinux)
         .Executes(() => {
             Log.Information("Finish");
         });
     
     private void Publish(string runtime) {
         Log.Information("Publishing {Runtime}", runtime);
         DotNetPublish(s => s
             .SetSelfContained(false)
             .SetPublishSingleFile(true)
             .SetRuntime(runtime)
             .SetOutput(OutputDirectory / runtime)
             .SetProject(Solution.nuke_test_avalonia)
             .SetConfiguration(Configuration));
     }
}// Clean → Restore → Publish → ZipArtifacts → CreateGitHubRelease