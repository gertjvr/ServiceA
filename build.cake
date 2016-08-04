#addin "Cake.FileHelpers"

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument<string>("target", "Default");
var configuration = Argument<string>("configuration", "Release");

//////////////////////////////////////////////////////////////////////
// EXTERNAL TOOLS
//////////////////////////////////////////////////////////////////////

#tool "nuget:?package=GitVersion.CommandLine"
#Tool "nuget:?package=xunit.runner.console"

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////

var projectName = "ServiceA";

string buildNumber = null;
string nugetVersion = null;
string preReleaseTag = null;
string semVersion = null;

var solutions = GetFiles("./**/*.sln");
var solutionPaths = solutions.Select(solution => solution.GetDirectory());

// Define directories.
var srcDir = Directory("./src");
var artifactsDir = Directory("../artifacts");
var testResultsDir = artifactsDir + Directory("test-results");
var nupkgDir = artifactsDir + Directory("nupkg");

var appConfig = srcDir + File("App.config.user");
var globalAssemblyFile = srcDir + File("GlobalAssemblyInfo.cs");

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(() =>
{
    Information("Service A");
    Information("");
});

Teardown(() =>
{
    Information("Finished running tasks.");
});

//////////////////////////////////////////////////////////////////////
// PRIVATE TASKS
//////////////////////////////////////////////////////////////////////

Task("__Build")
    .IsDependentOn("__Clean")
    .IsDependentOn("__RestoreNugetPackages")
    .IsDependentOn("__UpdateAssemblyVersionInformation")
    .IsDependentOn("__BuildSolutions")
    .IsDependentOn("__RunTests")
    .IsDependentOn("__CreateNuGetPackages");

Task("__Clean")
    .Does(() =>
{
    CleanDirectories(new DirectoryPath[] {
        artifactsDir,
        testResultsDir,
        nupkgDir
    });

    foreach(var path in solutionPaths)
    {
        Information("Cleaning {0}", path);
        CleanDirectories(path + "/**/bin/" + configuration);
        CleanDirectories(path + "/**/obj/" + configuration);
    }
});

Task("__RestoreNugetPackages")
    .Does(() =>
{
    Information("Restoring NuGet Packages");

	var settings = new ProcessSettings()
		.UseWorkingDirectory(srcDir)
		.WithArguments(arguments => arguments.Append("restore"));

	using(var process = StartAndReturnProcess(srcDir + File(".paket\\paket.exe"), settings))
	{
		process.WaitForExit();
		// This should output 0 as valid arguments supplied
		if(process.GetExitCode() != 0) 
		{
			throw new CakeException("Failed to restore nuget packages.");
		}
	}
});

Task("__CreateNuGetPackages")
    .Does(() =>
{
	var settings = new ProcessSettings()
		.UseWorkingDirectory(srcDir)
		.WithArguments(arguments => 
			arguments
				.Append("pack")
				.Append("output {0}", nupkgDir)
				.Append("buildconfig {0}", configuration)
				.Append("buildplatform {0}", "AnyCPU")
				.Append("version {0}", buildNumber)
				.Append("include-referenced-projects")
			);

	using(var process = StartAndReturnProcess(srcDir + File(".paket\\paket.exe"), settings))
	{
		process.WaitForExit();
		// This should output 0 as valid arguments supplied
		if(process.GetExitCode() != 0) 
		{
			throw new CakeException("Failed to create nuget packages.");
		}
	}
});

Task("__UpdateAssemblyVersionInformation")
    .Does(() =>
{
	CreateAssemblyInfo(globalAssemblyFile, new AssemblyInfoSettings {
        Product = projectName,
        Description = projectName,
        Company = "Solutions",
        Copyright = "Copyright (c) " + DateTime.Now.Year
    });

	GitVersion(new GitVersionSettings
    {
        UpdateAssemblyInfo = true,
		UpdateAssemblyInfoFilePath = globalAssemblyFile,
        LogFilePath = "console",
        OutputType = GitVersionOutput.BuildServer,
    });

    var assertedVersions = GitVersion(new GitVersionSettings
    {
        OutputType = GitVersionOutput.Json,
    });

	buildNumber = assertedVersions.MajorMinorPatch;
    nugetVersion = assertedVersions.NuGetVersion;
    preReleaseTag = assertedVersions.PreReleaseTag;
    semVersion = assertedVersions.LegacySemVerPadded;

    Information("Updating assembly version to {0}", buildNumber);
});

Task("__BuildSolutions")
    .Does(() =>
{
    if (!FileExists(appConfig))
    {
        FileWriteText(appConfig, @"<appSettings></appSettings>");
    }

    foreach(var solution in solutions)
    {
        Information("Building {0}", solution);

        MSBuild(solution, settings =>
            settings
                .SetConfiguration(configuration)
                .WithProperty("TreatWarningsAsErrors", "true")
                .WithProperty("RunOctoPack", "true")
                .WithProperty("OctoPackPublishPackageToFileShare", MakeAbsolute(nupkgDir).ToString())
                .WithProperty("OctoPackPublishPackagesToTeamCity", "false")
                .UseToolVersion(MSBuildToolVersion.NET46)
                .SetVerbosity(Verbosity.Minimal)
                .SetNodeReuse(false));
    }
});

Task("__RunTests")
    .Does(() =>
{
    var settings = new XUnit2Settings {
        OutputDirectory = testResultsDir,
        XmlReportV1 = true
    };

    settings.ExcludeTrait("Category", new [] { "IntegrationTests" } );

    XUnit2("./src/**/bin/" + configuration + "/*.*Tests.dll", settings);
});

///////////////////////////////////////////////////////////////////////////////
// PRIMARY TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("__Build");

///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);
