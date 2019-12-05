﻿using System;
using System.Collections.Generic;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.WindowsAzure.Storage;

using NUnit.Framework;
using NUnit.Framework.Interfaces;

using Orleans;
using Orleans.Hosting;
using Orleans.Configuration;
using Orleans.Storage;

using Orleankka.Testing;
using Orleankka.Cluster;
using Orleankka.Features.Intercepting_requests;
using Orleankka.Legacy.Cluster;

using Orleans.Providers.Streams.AzureQueue;
using Orleans.Streaming;

[assembly: TeardownSilo]

namespace Orleankka.Testing
{
    using Features.State_persistence;
    using Orleans.Runtime;

    [AttributeUsage(AttributeTargets.Class)]
    public class RequiresSiloAttribute : TestActionAttribute
    {
        const string DemoClusterId = "localhost-demo";
        const string DemoServiceId = "localhost-demo-service";

        const int LocalhostSiloPort = 11111;
        const int LocalhostGatewayPort = 30000;
        static readonly IPAddress LocalhostSiloAddress = IPAddress.Loopback;

        public override void BeforeTest(ITest test)
        {
            if (!test.IsSuite)
                return;

            if (TestActorSystem.Instance != null)
                return;

            var sb = new SiloHostBuilder()
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = DemoClusterId;
                    options.ServiceId = DemoServiceId;
                })
                .Configure<SchedulingOptions>(options =>
                {
                    options.AllowCallChainReentrancy = false;
                    options.PerformDeadlockDetection = true;
                })
                .UseDevelopmentClustering(options => options.PrimarySiloEndpoint = new IPEndPoint(LocalhostSiloAddress, LocalhostSiloPort))
                .ConfigureEndpoints(LocalhostSiloAddress, LocalhostSiloPort, LocalhostGatewayPort)
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .UseInMemoryReminderService()
                .AddAzureQueueStreams("aqp", (SiloAzureQueueStreamConfigurator<AzureQueueDataAdapterV2> x) =>
                {
                    x.ConfigureCache(1024);
                    x.ConfigureAzureQueue(b => b.Configure(options =>
                    {
                        options.ConnectionString = CloudStorageAccount.DevelopmentStorageAccount.ToString();
                        options.MessageVisibilityTimeout = TimeSpan.FromMinutes(1);
                        options.QueueNames = new List<string>{"aqp1", "aqp2", "aqp3", "aqp4"};
                    }));
                })
                .ConfigureServices(services =>
                {
                    services.AddSingletonNamedService<IGrainStorage>("test", (sp, name) => new TestStorageProvider(name));
                    services.Configure<GrainCollectionOptions>(options => options.CollectionAge = TimeSpan.FromMinutes(1.1));
                })
                .ConfigureApplicationParts(x => x
                    .AddApplicationPart(GetType().Assembly)
                    .AddApplicationPart(typeof(MemoryGrainStorage).Assembly)
                    .WithCodeGeneration())
                .UseOrleankka(x => x
                    .ActorMiddleware(typeof(TestActorBase), new TestActorMiddleware())
                    .ActorRefMiddleware(new TestActorRefMiddleware()))
                .UseOrleankkaLegacyFeatures(x => x
                    .AddSimpleMessageStreamProvider("sms")
                    .RegisterPersistentStreamProviders("aqp"));

            var host = sb.Build();
            host.StartAsync().Wait();

            TestActorSystem.Host = host;
            TestActorSystem.Client = host.Services.GetRequiredService<IClusterClient>();
            TestActorSystem.Instance = host.ActorSystem();
        }
    }

    public class TeardownSiloAttribute : TestActionAttribute
    {
        public override void AfterTest(ITest test)
        {
            if (!test.IsSuite)
                return;

            if (TestActorSystem.Instance == null)
                return;

            var timeout = TimeSpan.FromSeconds(5);

            TestActorSystem.Client.Close().Wait(timeout);
            TestActorSystem.Client.Dispose();
            
            TestActorSystem.Host.StopAsync().Wait(timeout);
            TestActorSystem.Host.Dispose();

            TestActorSystem.Client = null;
            TestActorSystem.Host = null;
            TestActorSystem.Instance = null;
        }
    }
}
