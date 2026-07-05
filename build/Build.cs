using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Components;
using Serilog;
using Tasks;
using static Tasks.HachimiPackagingTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions("continuous",
    GitHubActionsImage.MacOsLatest,
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = true,
    PublishArtifacts = true,
    EnableGitHubToken = true,
    On = [GitHubActionsTrigger.Push],
    InvokedTargets = [nameof(ICreateGitHubRelease.CreateGitHubRelease)])]
public class Build : NukeBuild, ICreateGitHubRelease {
    [Solution(GenerateProjects = true)] Solution Solution;
    
    private readonly AbsolutePath OutputDirectory = RootDirectory / "artifacts";
    private readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    public bool PreRelease => Configuration == Configuration.Debug;
    public string Name => $"AutoBuild-{Configuration}-{Solution.nuke_test_avalonia.GetProperty("Version")}";
    public IEnumerable<AbsolutePath> AssetFiles => OutputDirectory.GetFiles();
    
    public static int Main() => Execute<Build>(x => ((ICreateGitHubRelease)x).CreateGitHubRelease);

    Target Clean => _ => _
        .Executes(() => {
            foreach (var file in OutputDirectory.GetFiles()) {
                Log.Information("Cleaning {File}", file);
            }
            
            OutputDirectory.CreateOrCleanDirectory();
            Log.Information("Clean up");
        });

     Target PublishWindows => _ => _
         .DependsOn(Clean)
         .WhenSkipped(DependencyBehavior.Skip)
         .OnlyWhenDynamic(OperatingSystem.IsLinux)
         .Executes(
             () => Publish(DotNetRuntimeIdentifier.win_x64), 
             () => Publish(DotNetRuntimeIdentifier.win_x86),
             () => Publish(DotNetRuntimeIdentifier.win_arm64));
     
     Target PublishMacOS => _ => _
         .DependsOn(Clean)
         .OnlyWhenDynamic(OperatingSystem.IsMacOS)
         .Executes(
             () => Publish(DotNetRuntimeIdentifier.osx_x64), 
             () => Publish("osx-arm64"));
     
     Target PublishLinux => _ => _
         .DependsOn(Clean)
         .OnlyWhenDynamic(OperatingSystem.IsLinux)
         .Executes(
             () => Publish(DotNetRuntimeIdentifier.linux_x64), 
             () => Publish(DotNetRuntimeIdentifier.linux_arm), 
             () => Publish(DotNetRuntimeIdentifier.linux_arm64));

     Target InstallTool => _ => _
         .DependsOn(PublishLinux, PublishMacOS, PublishWindows)
         .Executes(() => {
             DotNetToolInstall(x => x
                 .SetPackageName("Hachimi.Packaging.Cli")
                 .EnableGlobal());
         });

     Target PackWindows => _ => _
         .DependsOn(InstallTool)
         .OnlyWhenDynamic(OperatingSystem.IsLinux)
         .Executes(
             () => Zip(DotNetRuntimeIdentifier.win_x64),
             () => Zip(DotNetRuntimeIdentifier.win_x86),
             () => Zip(DotNetRuntimeIdentifier.win_arm64));
     
     Target PackLinux => _ => _
         .DependsOn(InstallTool)
         .OnlyWhenDynamic(OperatingSystem.IsLinux)
         .Executes(
             () => AppImage(DotNetRuntimeIdentifier.linux_x64),
             () => AppImage(DotNetRuntimeIdentifier.linux_arm),
             () => AppImage(DotNetRuntimeIdentifier.linux_arm64));

     Target PackMacOS => _ => _
         .DependsOn(InstallTool)
         .OnlyWhenDynamic(OperatingSystem.IsMacOS)
         .Executes(
             () => AppBundle("osx-arm64"),
             () => AppBundle(DotNetRuntimeIdentifier.osx_x64));

     Target ICreateGitHubRelease.CreateGitHubRelease => _ => _
         .Inherit<ICreateGitHubRelease>()
         .WhenSkipped(DependencyBehavior.Skip)
         .DependsOn(PackWindows, PackLinux, PackMacOS)
         .OnlyWhenStatic(() => IsServerBuild);

     // Target Finish => _ => _ 
     //     .DependsOn(PackWindows, PackLinux, PackMacOS)
     //     .Executes(() => {
     //         Log.Information("Finish");
     //     });

     private void Publish(string runtime) {
         Log.Information("Publishing {Runtime}", runtime);
         DotNetPublish(s => s
             .SetSelfContained(false)
             .SetPublishSingleFile(true)
             .SetRuntime(runtime)
             .SetOutput(OutputDirectory / runtime)
             .SetProject(Solution.nuke_test_avalonia)
             .SetConfiguration(Configuration)
             .SetProperty("DebugType", "none")
             .SetProperty("DebugSymbols", "false"));
         
         Log.Information("Clean pdb file...");
         foreach (var file in Directory.EnumerateFiles(OutputDirectory / runtime, "*.pdb", SearchOption.AllDirectories))
             File.Delete(file);
     }

     private void Zip(string runtime) {
         HachimiPackagingPortable(x => x
             .SetSourceDirectory(OutputDirectory / runtime)
             .SetOutputFile(OutputDirectory / $"{Solution.nuke_test_avalonia.Name}-{runtime}.zip")
             .SetRuntime(runtime));
     }

     private void AppImage(string runtime) {
         var arch = runtime switch {
             "linux-x64" => Architecture.X64,
             "linux-arm" => Architecture.Arm,
             "linux-arm64" => Architecture.Arm64,
             _ => throw new NotSupportedException()
         };
         
         HachimiPackagingAppImage(x => x
             .SetDisplayName("Nuke Test App")
             .SetSourceDirectory(OutputDirectory / runtime)
             .SetAppName(Solution.nuke_test_avalonia.Name)
             .SetDescription("hello appimage")
             .SetOutputFile(OutputDirectory / $"{Solution.nuke_test_avalonia.Name}-{runtime}.AppImage")
             .SetIcon(Solution.nuke_test_avalonia.Directory / "Assets" / "icon.png")
             .SetArchitecture(arch)
             .DisableTerminal());
     }

     private void AppBundle(string runtime) {
         HachimiPackagingAppBundle(x => x
             .SetDisplayName("Nuke Test App")
             .SetVersion("1.0.0")
             .SetSourceDirectory(OutputDirectory / runtime)
             .SetOutputFile(OutputDirectory / $"{Solution.nuke_test_avalonia.Name}-{runtime}.zip")
             .SetAppName(Solution.nuke_test_avalonia.Name)
             .SetIdentifier("com.lunova.nuketest")
             .SetPrincipalClass("NSApplication")
             .SetIcon(Solution.nuke_test_avalonia.Directory / "Assets" / "icon.icns")
             .EnableHighResolutionCapable());
     }
    
}// Clean → Restore → Publish → ZipArtifacts → CreateGitHubRelease

[Command(
    Arguments = "appimage",
    Type = typeof(HachimiPackagingTasks),
    Command = nameof(HachimiPackagingAppImage))]
public class PackagingAppImageSettings : ToolOptions {
    /// <summary>
    /// Published application directory (for example: <c>bin/Release/tfm/publish</c>)
    /// </summary>
    [Argument(Format = "-s {value}")] 
    public string SourceDirectory => Get<string>(() => SourceDirectory);
   
    /// <summary>
    /// Destination path for the generated <c>.AppImage</c> file
    /// </summary>
    [Argument(Format = "-o {value}")]
    public string OutputFile => Get<string>(() => OutputFile);
    
    /// <summary>
    /// Application name
    /// </summary>
    [Argument(Format = "-a {value}")]
    public string AppName => Get<string>(() => AppName);
    
    /// <summary>
    /// Application name
    /// </summary>
    [Argument(Format = "-dn {value}")]
    public string DisplayName => Get<string>(() => DisplayName);
    
    /// <summary>
    /// Application name
    /// </summary>
    [Argument(Format = "--arch {value}")]
    public Architecture Architecture => Get<Architecture>(() => Architecture);
    
    /// <summary>
    /// Summary. Short description that should not end in a dot.
    /// </summary>
    [Argument(Format = "-d {value}")]
    public string Description => Get<string>(() => Description);
    
    /// <summary>
    /// Path to the application icon
    /// </summary>
    [Argument(Format = "-i {value}")]
    public string Icon => Get<string>(() => Icon);
    
    [Argument(Format = "-t {value}")]
    public bool IsTerminal => Get<bool>(() => IsTerminal);
}

[Command(
    Arguments = "portable",
    Type = typeof(HachimiPackagingTasks),
    Command = nameof(HachimiPackagingPortable))]
public class PackagingPortableSettings : ToolOptions {
    /// <summary>
    /// Destination path for the generated <c>.AppImage</c> file
    /// </summary>
    [Argument(Format = "-o {value}")]
    public string OutputFile => Get<string>(() => OutputFile);
    
    /// <summary>
    /// Published application directory (for example: <c>bin/Release/tfm/publish</c>)
    /// </summary>
    [Argument(Format = "-s {value}")] 
    public string SourceDirectory => Get<string>(() => SourceDirectory);
    
    /// <summary>
    /// Application name
    /// </summary>
    [Argument(Format = "-a {value}")]
    public string AppName => Get<string>(() => AppName);
    
    [Argument(Format = "-r {value}")]
    public string Runtime => Get<string>(() => Runtime);
}

[Command(
    Arguments = "app",
    Type = typeof(HachimiPackagingTasks),
    Command = nameof(HachimiPackagingAppBundle))]
public class PackagingAppBundleSettings : ToolOptions {
    /// <summary>
    /// Destination path for the generated <c>.AppImage</c> file
    /// </summary>
    [Argument(Format = "-o {value}")]
    public string OutputFile => Get<string>(() => OutputFile);
    
    /// <summary>
    /// Published application directory (for example: <c>bin/Release/tfm/publish</c>)
    /// </summary>
    [Argument(Format = "-s {value}")] 
    public string SourceDirectory => Get<string>(() => SourceDirectory);
    
    /// <summary>
    /// Application name
    /// </summary>
    [Argument(Format = "-a {value}")]
    public string AppName => Get<string>(() => AppName);
    
    [Argument(Format = "--icon {value}")]
    public string Icon => Get<string>(() => Icon);
    
    [Argument(Format = "-v {value}")]
    public string Version => Get<string>(() => Version);
    
    [Argument(Format = "-i {value}")]
    public string Identifier => Get<string>(() => Identifier);
    
    [Argument(Format = "-dn {value}")]
    public string DisplayName => Get<string>(() => DisplayName);
    
    [Argument(Format = "-pc {value}")]
    public string PrincipalClass => Get<string>(() => PrincipalClass);
    
    [Argument(Format = "-hrc {value}")]
    public bool IsHighResolutionCapable => Get<bool>(() => IsHighResolutionCapable);
}