using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace nuget_mirror
{
    class Program
    {
        // Usage nuget-mirror -package {package} -source {sourceName} -destination {destinationName} -apikey {apikey}

        static async Task Main(string[] args)
        {
            try
            {
                var commandLineParser = new SimpleCommandLineParser();
                commandLineParser.Parse(args);
                var package = commandLineParser.Arguments["package"][0];
                var sourceFeedName = commandLineParser.Arguments["source"][0];
                var destinationFeedName = commandLineParser.Arguments["destination"][0];
                var apiKey = commandLineParser.Arguments["apikey"][0];

                // Get package sources from configs
                var settings = Settings.LoadDefaultSettings(Assembly.GetEntryAssembly().Location);
                var sourceRepositoryProvider = new SourceRepositoryProvider(settings, Repository.Provider.GetCoreV3());
                var packageSources = sourceRepositoryProvider.PackageSourceProvider.LoadPackageSources().ToArray();
                var sourcePackageSource = packageSources.Single(s => s.Name == sourceFeedName);
                var destinationPackageSource = packageSources.Single(s => s.Name == destinationFeedName);

                // Source feed setup
                var sourceRepository = sourceRepositoryProvider.CreateRepository(sourcePackageSource,FeedType.HttpV3);
                var sourceHttpSource = HttpSource.Create(sourceRepository);
                var sourcePackagesFind = new RemoteV3FindPackageByIdResource(sourceRepository, sourceHttpSource);

                // Destination feed setup
                var destinationRepository = sourceRepositoryProvider.CreateRepository(destinationPackageSource, FeedType.HttpV3);
                var destinationHttpSource = HttpSource.Create(destinationRepository);
                var destinationPackages =
                    new RemoteV3FindPackageByIdResource(destinationRepository, destinationHttpSource);
                var updateResource = await destinationRepository.GetResourceAsync<PackageUpdateResource>();

                var logger = new NullLogger();

                using (var sourceCacheContext = new SourceCacheContext())
                using (var destinationCacheContext = new SourceCacheContext())
                {
                    // List all versions from source
                    var sourcePackageVersions = (await sourcePackagesFind
                            .GetAllVersionsAsync(package, sourceCacheContext, logger,
                                CancellationToken.None))
                        .Select(p => p.OriginalVersion);

                    // List all versions from destination
                    var destinationPackageVersions = (await destinationPackages
                            .GetAllVersionsAsync(package, destinationCacheContext, logger,
                                CancellationToken.None))
                        .Select(p => p.OriginalVersion);

                    // See what versions are missing
                    var missingVersions = sourcePackageVersions
                        .Where(version => !destinationPackageVersions.Contains(version))
                        .ToArray();

                    // Push missing versions
                    var tempPath = Path.GetTempPath();
                    foreach (var version in missingVersions)
                    {
                        Console.WriteLine($"Mirroring {package}.{version}...");
                        // download
                        var packageDownloader = await sourcePackagesFind.GetPackageDownloaderAsync(
                            new PackageIdentity(package, new NuGetVersion(version)),
                            sourceCacheContext, logger, CancellationToken.None);
                        var destinationFilePath = Path.Combine(tempPath, $"OwinHttpMessageHandler.{version}.nupkg");
                        await packageDownloader.CopyNupkgFileToAsync(destinationFilePath, CancellationToken.None);

                        // push
                        await updateResource.Push(destinationFilePath, null, 600, false, s => apiKey, _ => null,
                            logger);
                    }
                }
                Console.WriteLine("Complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
