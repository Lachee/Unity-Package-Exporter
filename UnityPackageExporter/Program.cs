using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityPackageExporter.Dependency;
using UnityPackageExporter.Package;

//-project "C:\Users\TasGDS\Documents\GitHub\discord-rpc-csharp\Unity Example\\" -dir "Assets\\"
//-project bin/ExampleProject/ -unpack package.unitypackage
namespace UnityPackageExporter
{
    class Program
    {

        private static readonly NLog.Logger Logger = NLog.LogManager.GetLogger("UnityPackageExporter");

        static int Main(string[] args)
        {
            var rootCommand = new RootCommand("Manages Unity packages and assets");

            var packCommand = BuildPackCommand();
            rootCommand.AddCommand(packCommand);

            var findCommand = BuildFindCommand();
            rootCommand.AddCommand(findCommand);

            var referenceCommand = BuildReferenceCommand();
            rootCommand.AddCommand(referenceCommand);

            return rootCommand.Invoke(args);
        }

        static Command BuildReferenceCommand()
        {
            var sourceArg = new Argument<DirectoryInfo>(name: "source", description: "Unity Project Direcotry.");
            var referenceOp = new Option<IEnumerable<string>>(
                aliases: new[] { "--assets", "-a" },
                description: "Scans an asset for all referenced files. Supports glob matching.",
                getDefaultValue: () => new[] { "**.*" }
            );
            var deepOp = new Option<bool>(
                aliases: new[] { "--deep", "-d" },
                description: "Should the scan be deep and search the reference's references?",
                getDefaultValue: () => false
            );
            var verboseOpt = new Option<NLog.LogLevel>(
                aliases: new[] { "--verbose", "--log-level", "-v" },
                description: "Sets the logging level",
                getDefaultValue: () => NLog.LogLevel.Info
            );

            var command = new Command(name: "references", description: "Finds all references for the given assets")
            {
                sourceArg,
                referenceOp,
                deepOp,
                verboseOpt,
            };

            command.SetHandler(async (DirectoryInfo source, IEnumerable<string> referencePatterns, bool deep, NLog.LogLevel verbose) =>
            {
                var config = new NLog.Config.LoggingConfiguration();
                var consoleTarget = new NLog.Targets.ConsoleTarget
                {
                    Name = "console",
                    Layout = "${time}|${level:uppercase=true}|${logger}|${message}",
                };
                config.AddRule(verbose, NLog.LogLevel.Fatal, consoleTarget);
                NLog.LogManager.Configuration = config;

                await Logger.Swallow(async () =>
                {
                    // Prepare the analyser
                    AssetAnalyser analyser = new AssetAnalyser(source.FullName);
                    Matcher assetMatcher = new Matcher();
                    assetMatcher.AddInclude("**/*.meta");
                    var assetFiles = assetMatcher.GetResultsInFullPath(source.FullName);
                    await analyser.AddFilesAsync(assetFiles);

                    // Search for all the References
                    Matcher referenceMatcher = new Matcher();
                    referenceMatcher.AddIncludePatterns(referencePatterns);

                    var refFiles = referenceMatcher.GetResultsInFullPath(source.FullName);
                    var files = await analyser.FindAllReferencesAsync(refFiles, deep);
                    foreach (var file in files)
                    {
                        Logger.Info(file);
                    }
                });
            }, sourceArg, referenceOp, deepOp, verboseOpt);
            return command;
        }

        /// <summary>Builds the find command</summary>
        static Command BuildFindCommand()
        {
            var sourceArg = new Argument<DirectoryInfo>(name: "source", description: "Unity Project Direcotry.");

            var guidOp = new Option<IEnumerable<string>>(
                aliases: new[] { "--guid", "-g" },
                description: "GUID to scan for",
                getDefaultValue: () => new string[0]
            );
            var referenceOp = new Option<IEnumerable<string>>(
                aliases: new[] { "--assets", "-a" },
                description: "Scans an asset for all referenced files. Supports glob matching.",
                getDefaultValue: () => new[] { "**.*" }
            );
            var verboseOpt = new Option<NLog.LogLevel>(
                aliases: new[] { "--verbose", "--log-level", "-v" },
                description: "Sets the logging level",
                getDefaultValue: () => NLog.LogLevel.Info
            );

            var command = new Command(name: "find", description: "Finds the GUIDs")
            {
                sourceArg,
                guidOp,
                referenceOp,
                verboseOpt,
            };

            command.SetHandler(async (DirectoryInfo source, IEnumerable<string> guids, IEnumerable<string> referencePatterns, NLog.LogLevel verbose) =>
            {
                var config = new NLog.Config.LoggingConfiguration();
                var consoleTarget = new NLog.Targets.ConsoleTarget
                {
                    Name = "console",
                    Layout = "${time}|${level:uppercase=true}|${logger}|${message}",
                };
                config.AddRule(verbose, NLog.LogLevel.Fatal, consoleTarget);
                NLog.LogManager.Configuration = config;

                await Logger.Swallow(async () =>
                {
                    // Prepare the analyser
                    AssetAnalyser analyser = new AssetAnalyser(source.FullName);
                    Matcher assetMatcher = new Matcher();
                    assetMatcher.AddInclude("**/*.meta");
                    var assetFiles = assetMatcher.GetResultsInFullPath(source.FullName);
                    await analyser.AddFilesAsync(assetFiles);

                    // Search for all the GUIDs
                    foreach (var guid in guids)
                    {
                        Logger.Trace($"Searching for GUID '{guid}'");
                        FileInfo file = analyser.FindFile(guid);
                        if (file != null)
                            Logger.Info($"{guid}: {file.FullName}");
                        else
                            Logger.Info($"{guid}: ");
                    }

                    // Search for all the References
                    Matcher referenceMatcher = new Matcher();
                    referenceMatcher.AddIncludePatterns(referencePatterns);
                    var refFiles = referenceMatcher.GetResultsInFullPath(source.FullName);
                    var refGUIDS = await analyser.FindAllGUIDDependenciesAsnyc(refFiles);
                    foreach (var guid in refGUIDS)
                    {
                        Logger.Trace($"Searching for Reference '{guid}'");
                        FileInfo file = analyser.FindFile(guid);
                        if (file != null)
                            Logger.Info($"{guid}: {file.FullName}");
                        else
                            Logger.Info($"{guid}: ");
                    }
                });
            }, sourceArg, guidOp, referenceOp, verboseOpt);
            return command;
        }

        /// <summary>Builds the pack command</summary>
        static Command BuildPackCommand()
        {
            var sourceArg = new Argument<DirectoryInfo>(name: "source", description: "Unity Project Direcotry.");
            var outputArg = new Argument<FileInfo>(name: "output", description: "Output .unitypackage file");

            var assetPatternOpt = new Option<IEnumerable<string>>(
                aliases: new[] { "--assets", "-a" },
                description: "Adds an asset to the pack. Supports glob matching.",
                getDefaultValue: () => new[] { "**.*" }
            );

            var excludePatternOpt = new Option<IEnumerable<string>>(
                aliases: new[] { "--exclude", "-e" },
                description: "Excludes an asset from the pack. Supports glob matching.",
                getDefaultValue: () => new[] { "Library/**.*", "**/.*" }
            );

            var skipDepOpt = new Option<bool>(
                aliases: new[] { "--skip-dependecy-check" },
                description: "Skips dependency analysis. Disabling this feature may result in missing assets in your packages.",
                getDefaultValue: () => false
            );

            var assetRootOpt = new Option<string>(
                aliases: new[] { "--asset-root", "-r" },
                description: "Sets the root directory for the assets. Used in dependency analysis to only check files that could be potentially included.",
                getDefaultValue: () => "Assets"
            );

            var verboseOpt = new Option<NLog.LogLevel>(
                aliases: new[] { "--verbose", "--log-level", "-v" },
                description: "Sets the logging level",
                getDefaultValue: () => NLog.LogLevel.Info
            );

            //var command = new Command(name: "pack", description: "Packs the assets in a Unity Project")
            var command = new Command(name: "pack", description: "Packs the assets in a Unity Project")
            {
                sourceArg,
                outputArg,
                assetPatternOpt, 
                excludePatternOpt,
                skipDepOpt,
                assetRootOpt,
                verboseOpt,
            };

            command.SetHandler(async (DirectoryInfo source, FileInfo output, IEnumerable<string> assetPattern, IEnumerable<string> excludePattern, bool skipDep, string assetRoot, NLog.LogLevel verbose) =>
            {
                // Setup the logger
                // TODO: Make logger setup a middleware in command builder
                var config = new NLog.Config.LoggingConfiguration();
                var consoleTarget = new NLog.Targets.ConsoleTarget
                {
                    Name = "console",
                    Layout = "${time}|${level:uppercase=true}|${logger}|${message}",
                };
                config.AddRule(verbose, NLog.LogLevel.Fatal, consoleTarget);
                NLog.LogManager.Configuration = config;

                await Logger.Swallow(async () =>
                {
                    Logger.Info("Packing {0}", source.FullName);

                    // Make the output file (touch it) so we can exclude
                    await File.WriteAllBytesAsync(output.FullName, new byte[0]);

                    Stopwatch timer = Stopwatch.StartNew();
                    using DependencyAnalyser analyser = !skipDep ? await DependencyAnalyser.CreateAsync(Path.Combine(source.FullName, assetRoot), excludePattern) : null;
                    using Packer packer = new Packer(source.FullName, output.FullName);

                    // Match all the assets we need
                    Matcher assetMatcher = new Matcher();
                    assetMatcher.AddIncludePatterns(assetPattern);
                    assetMatcher.AddExcludePatterns(excludePattern);
                    assetMatcher.AddExclude(output.Name);

                    var matchedAssets = assetMatcher.GetResultsInFullPath(source.FullName);
                    await packer.AddAssetsAsync(matchedAssets);

                    if (!skipDep)
                    {
                        var results = await analyser.FindDependenciesAsync(matchedAssets);
                        await packer.AddAssetsAsync(results);
                    }

                    // Finally flush and tell them we done
                    //await packer.FlushAsync();
                    Logger.Info("Finished Packing in {0}ms", timer.ElapsedMilliseconds);
                });
            }, sourceArg, outputArg, assetPatternOpt, excludePatternOpt, skipDepOpt, assetRootOpt, verboseOpt);

            return command;
        }

    }
}