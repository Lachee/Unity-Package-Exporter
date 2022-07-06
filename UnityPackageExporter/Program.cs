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
            var command = BuildCommands();
            return command.Invoke(args);
        }

        /// <summary>Builds the pack command</summary>
        static RootCommand BuildCommands()
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
                getDefaultValue: () => NLog.LogLevel.Trace
            );

            //var command = new Command(name: "pack", description: "Packs the assets in a Unity Project")
            var command = new RootCommand(description: "Packs the assets in a Unity Project")
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