﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using NUnit.Framework;

using Orleans;

namespace Orleankka.Legacy.Features
{
    namespace Declarative_stream_subscriptions
    {
        using Meta;
        using Legacy;
        using Testing;

        [Serializable]
        public class Received : Query<List<string>>
        {}

        [Serializable]
        public class Deactivate : Command
        {}

        public abstract class TestConsumerActorBase : Actor
        {
            protected readonly List<string> received = new List<string>();

            void On(string x) => received.Add(x);
            List<string> On(Received x) => received;

            void On(Deactivate x) => Activation.DeactivateOnIdle();
        }

        [Serializable]
        public class Push : Command
        {
            public readonly StreamRef Stream;
            public readonly object Item;

            public Push(StreamRef stream, object item)
            {
                Stream = stream;
                Item = item;
            }
        }

        public interface ITestProducerActor : IActorGrain
        {}

        public class TestProducerActor : Actor, ITestProducerActor
        {
            Task On(Push x) => x.Stream.Push(x.Item);
        }

        class TestCases
        {
            readonly string provider;
            readonly TimeSpan timeout;
            readonly IActorSystem system;

            public TestCases(string provider, TimeSpan timeout)
            {
                this.provider = provider;
                this.timeout = timeout;
                system = TestActorSystem.Instance;
            }

            public async Task Client_to_stream<T>() where T : IActorGrain
            {
                var stream = system.StreamOf(provider, "cs");

                await stream.Push("ce");
                await Task.Delay(timeout);

                var consumer = system.ActorOf<T>("#");
                var received = await consumer.Ask(new Received());
                Assert.That(received, Is.EquivalentTo(new[] {"ce"}));
            }

            public async Task Actor_to_stream<T>() where T : IActorGrain
            {
                var stream = system.StreamOf(provider, "as");

                await Push(stream, "ae");
                await Task.Delay(timeout);

                var consumer = system.ActorOf<T>("#");
                var received = await consumer.Ask(new Received());
                Assert.That(received, Is.EquivalentTo(new[] {"ae"}));
            }

            public async Task Multistream_subscription_with_fixed_ids<T>() where T : IActorGrain
            {
                var a = system.StreamOf(provider, "a");
                var b = system.StreamOf(provider, "b");

                await Push(a, "a-001");
                await Push(b, "b-001");
                await Task.Delay(timeout);

                var consumer = system.ActorOf<T>("#");
                var received = await consumer.Ask(new Received());
                Assert.That(received, Is.EquivalentTo(new[] {"a-001", "b-001"}));
            }

            public async Task Multistream_subscription_based_on_regex_matching<T>() where T : IActorGrain
            {
                var s1 = system.StreamOf(provider, "INV-001");
                var s2 = system.StreamOf(provider, "INV-002");

                await Push(s1, "001");
                await Push(s2, "002");
                await Task.Delay(timeout);

                var consumer = system.ActorOf<T>("#");
                var received = await consumer.Ask(new Received());
                Assert.That(received, Is.EquivalentTo(new[] {"001", "002"}));
            }

            public async Task Declared_handler_only_automatic_item_filtering<T>() where T : IActorGrain
            {
                var stream = system.StreamOf(provider, "declared-auto");
                Assert.DoesNotThrowAsync(async ()=> await Push(stream, 123),
                    "Should not throw handler not found exception");

                await Push(stream, "e-123");
                await Task.Delay(timeout);

                var consumer = system.ActorOf<T>("#");
                var received = await consumer.Ask(new Received());
                Assert.That(received.Count, Is.EqualTo(1));
                Assert.That(received[0], Is.EqualTo("e-123"));
            }

            public async Task Select_all_filter<T>() where T : IActorGrain
            {
                var stream = system.StreamOf(provider, "select-all");

                await Push(stream, 42);
                await Task.Delay(timeout);

                var consumer = system.ActorOf<T>("#");
                var received = await consumer.Ask(new Received());

                Assert.That(received.Count, Is.EqualTo(1));
                Assert.That(received[0], Is.EqualTo("42"));
            }

            public async Task Explicit_filter<T>() where T : IActorGrain
            {
                var stream = system.StreamOf(provider, "filtered");

                await Push(stream, "f-001");
                await Push(stream, "f-002");
                await Task.Delay(timeout);

                var consumer = system.ActorOf<T>("#");
                var received = await consumer.Ask(new Received());
                Assert.That(received.Count, Is.EqualTo(0));
            }

            public async Task Dynamic_target_selection<T>() where T : IActorGrain
            {
                var stream = system.StreamOf(provider, "dynamic-target");

                await Push(stream, "red");
                await Push(stream, "blue");
                await Task.Delay(timeout);

                var consumer1 = system.ActorOf<T>("red-pill");
                var consumer2 = system.ActorOf<T>("blue-pill");

                Assert.That((await consumer1.Ask(new Received()))[0], Is.EqualTo("red"));
                Assert.That((await consumer2.Ask(new Received()))[0], Is.EqualTo("blue"));
            }

            public async Task Batch_receive<T>(bool unsupported = false) where T : IActorGrain, IGrainWithStringKey
            {
                var stream = system.StreamOf(provider, "batched");

                if (unsupported)
                { 
                    var e = Assert.ThrowsAsync<NotImplementedException>(async () => await stream.Push(new[] {"i1", "i2"}));
                    Assert.That(e.Message,
                        Is.EqualTo("We still don't support OnNextBatchAsync()"),
                        "Orleans still doesn't support batching in 2.4.4");
                    return;
                }

                await stream.Push(new[] {"i1", "i2"});
                await Task.Delay(timeout);

                var consumer = system.ActorOf<T>("#");
                var received = await consumer.Ask(new Received());
                Assert.That(received, 
                    Is.EquivalentTo(new[] {"i1", "i2"}), 
                    "The batch will be unrolled at a grain and items will be delivered to Receive one by one");
            }

            async Task Push(StreamRef stream, object item)
            {
                var producer = system.ActorOf<ITestProducerActor>("foo");
                await producer.Tell(new Push(stream, item));
            }
        }

        namespace SimpleMessageStreamProviderVerification
        {
            public interface ITestClientToStreamConsumerActor : IActorGrain
            { }

            [StreamSubscription(Source = "sms:cs", Target = "#")]
            public class TestClientToStreamConsumerActor : TestConsumerActorBase, ITestClientToStreamConsumerActor
            {}

            public interface ITestActorToStreamConsumerActor : IActorGrain
            { }

            [StreamSubscription(Source = "sms:as", Target = "#")]
            public class TestActorToStreamConsumerActor : TestConsumerActorBase, ITestActorToStreamConsumerActor
            {}

            public interface ITestMultistreamSubscriptionWithFixedIdsActor : IActorGrain
            { }

            [StreamSubscription(Source = "sms:a", Target = "#")]
            [StreamSubscription(Source = "sms:b", Target = "#")]
            public class TestMultistreamSubscriptionWithFixedIdsActor : TestConsumerActorBase, ITestMultistreamSubscriptionWithFixedIdsActor
            {}

            public interface ITestMultistreamRegexBasedSubscriptionActor : IActorGrain
            { }

            [StreamSubscription(Source = "sms:/INV-([0-9]+)/", Target = "#")]
            public class TestMultistreamRegexBasedSubscriptionActor : TestConsumerActorBase, ITestMultistreamRegexBasedSubscriptionActor
            {}

            public interface ITestDeclaredHandlerOnlyAutomaticFilterActor : IActorGrain
            { }

            [StreamSubscription(Source = "sms:declared-auto", Target = "#")]
            public class TestDeclaredHandlerOnlyAutomaticFilterActor : TestConsumerActorBase, ITestDeclaredHandlerOnlyAutomaticFilterActor
            {}

            public interface ITestSelectAllFilterActor : IActorGrain
            { }

            [StreamSubscription(Source = "sms:select-all", Target = "#", Filter = "*")]
            public class TestSelectAllFilterActor : TestConsumerActorBase, ITestSelectAllFilterActor
            {
                public override Task<object> OnReceive(object message)
                {
                    if (message is int)
                    {
                        received.Add(message.ToString());
                        return Task.FromResult<object>(null);
                    }

                    return base.OnReceive(message);
                }
            }

            public interface ITestExplicitFilterActor : IActorGrain
            {}

            [StreamSubscription(Source = "sms:filtered", Target = "#", Filter = "SelectItem()")]
            public class TestExplicitFilterActor : TestConsumerActorBase, ITestExplicitFilterActor
            {
                public static bool SelectItem(object item) => false;
            }

            public interface ITestDynamicTargetSelectorActor : IActorGrain
            { }

            [StreamSubscription(Source = "sms:dynamic-target", Target = "ComputeTarget()")]
            public class TestDynamicTargetSelectorActor : TestConsumerActorBase, ITestDynamicTargetSelectorActor
            {
                public static string ComputeTarget(object item) => $"{item}-pill";
            }

            public interface ITestBatchReceiveActor : IActorGrain, IGrainWithStringKey
            { }

            [StreamSubscription(Source = "sms:batched", Target = "#")]
            public class TestBatchReceiveActor : TestConsumerActorBase, ITestBatchReceiveActor
            {}

            [TestFixture, RequiresSilo]
            public class Tests
            {
                static TestCases Verify() =>
                   new TestCases("sms", TimeSpan.FromMilliseconds(100));

                [Test, Ignore("Declarative subscriptions are server-side only")]
                public async Task Client_to_stream()                                        => await Verify().Client_to_stream<ITestClientToStreamConsumerActor>();

                [Test] public async Task Actor_to_stream()                                  => await Verify().Actor_to_stream<ITestActorToStreamConsumerActor>();
                [Test] public async Task Multistream_subscription_with_fixed_ids()          => await Verify().Multistream_subscription_with_fixed_ids<ITestMultistreamSubscriptionWithFixedIdsActor>();
                [Test] public async Task Multistream_subscription_based_on_regex_matching() => await Verify().Multistream_subscription_based_on_regex_matching<ITestMultistreamRegexBasedSubscriptionActor>();
                [Test] public async Task Declared_handler_only_automatic_item_filtering()   => await Verify().Declared_handler_only_automatic_item_filtering<ITestDeclaredHandlerOnlyAutomaticFilterActor>();
                [Test] public async Task Select_all_filter()                                => await Verify().Select_all_filter<ITestSelectAllFilterActor>();
                [Test] public async Task Explicit_filter()                                  => await Verify().Explicit_filter<ITestExplicitFilterActor>();
                [Test] public async Task Dynamic_target_selection()                         => await Verify().Dynamic_target_selection<ITestDynamicTargetSelectorActor>();
                [Test] public async Task Batch_receive()                                    => await Verify().Batch_receive<ITestBatchReceiveActor>(unsupported: true);
            }
        }

        namespace AzureQueueStreamProviderVerification
        {
            public interface ITestClientToStreamConsumerActor : IActorGrain
            { }

            [StreamSubscription(Source = "aqp:cs", Target = "#")]
            public class TestClientToStreamConsumerActor : TestConsumerActorBase, ITestClientToStreamConsumerActor
            {}

            public interface ITestActorToStreamConsumerActor : IActorGrain
            { }

            [StreamSubscription(Source = "aqp:as", Target = "#")]
            public class TestActorToStreamConsumerActor : TestConsumerActorBase, ITestActorToStreamConsumerActor
            {}

            public interface ITestMultistreamSubscriptionWithFixedIdsActor : IActorGrain
            { }

            [StreamSubscription(Source = "aqp:a", Target = "#")]
            [StreamSubscription(Source = "aqp:b", Target = "#")]
            public class TestMultistreamSubscriptionWithFixedIdsActor : TestConsumerActorBase, ITestMultistreamSubscriptionWithFixedIdsActor
            {}

            public interface ITestMultistreamRegexBasedSubscriptionActor : IActorGrain
            { }

            [StreamSubscription(Source = "aqp:/INV-([0-9]+)/", Target = "#")]
            public class TestMultistreamRegexBasedSubscriptionActor : TestConsumerActorBase, ITestMultistreamRegexBasedSubscriptionActor
            {}

            public interface ITestDeclaredHandlerOnlyAutomaticFilterActor : IActorGrain
            { }

            [StreamSubscription(Source = "aqp:declared-auto", Target = "#")]
            public class TestDeclaredHandlerOnlyAutomaticFilterActor : TestConsumerActorBase, ITestDeclaredHandlerOnlyAutomaticFilterActor
            {}

            public interface ITestSelectAllFilterActor : IActorGrain
            {}

            [StreamSubscription(Source = "aqp:select-all", Target = "#", Filter = "*")]
            public class TestSelectAllFilterActor : TestConsumerActorBase, ITestSelectAllFilterActor
            {
                public override Task<object> OnReceive(object message)
                {
                    if (message is int)
                    {
                        received.Add(message.ToString());
                        return Task.FromResult<object>(null);
                    }

                    return base.OnReceive(message);
                }
            }

            public interface ITestExplicitFilterActor : IActorGrain
            { }

            [StreamSubscription(Source = "aqp:filtered", Target = "#", Filter = "SelectItem()")]
            public class TestExplicitFilterActor : TestConsumerActorBase, ITestExplicitFilterActor
            {
                public static bool SelectItem(object item) => false;
            }

            public interface ITestDynamicTargetSelectorActor : IActorGrain
            { }

            [StreamSubscription(Source = "aqp:dynamic-target", Target = "ComputeTarget()")]
            public class TestDynamicTargetSelectorActor : TestConsumerActorBase, ITestDynamicTargetSelectorActor
            {
                public static string ComputeTarget(object item) => $"{item}-pill";
            }

            public interface ITestBatchReceiveActor : IActorGrain, IGrainWithStringKey
            { }

            [StreamSubscription(Source = "aqp:batched", Target = "#")]
            public class TestBatchReceiveActor : TestConsumerActorBase, ITestBatchReceiveActor
            {}

            [TestFixture, RequiresSilo]
            [Category("Slow")]
            public class Tests
            {
               static TestCases Verify() =>
                  new TestCases("aqp", TimeSpan.FromSeconds(5));

                [Test] public async Task Client_to_stream()                                 => await Verify().Client_to_stream<ITestClientToStreamConsumerActor>();
                [Test] public async Task Actor_to_stream()                                  => await Verify().Actor_to_stream<ITestActorToStreamConsumerActor>();
                [Test] public async Task Multistream_subscription_with_fixed_ids()          => await Verify().Multistream_subscription_with_fixed_ids<ITestMultistreamSubscriptionWithFixedIdsActor>();
                [Test] public async Task Multistream_subscription_based_on_regex_matching() => await Verify().Multistream_subscription_based_on_regex_matching<ITestMultistreamRegexBasedSubscriptionActor>();
                [Test] public async Task Declared_handler_only_automatic_item_filtering()   => await Verify().Declared_handler_only_automatic_item_filtering<ITestDeclaredHandlerOnlyAutomaticFilterActor>();
                [Test] public async Task Select_all_filter()                                => await Verify().Select_all_filter<ITestSelectAllFilterActor>();
                [Test] public async Task Explicit_filter()                                  => await Verify().Explicit_filter<ITestExplicitFilterActor>();
                [Test] public async Task Dynamic_target_selection()                         => await Verify().Dynamic_target_selection<ITestDynamicTargetSelectorActor>();
                [Test] public async Task Batch_receive()                                    => await Verify().Batch_receive<ITestBatchReceiveActor>();
            }
        }
    }
}