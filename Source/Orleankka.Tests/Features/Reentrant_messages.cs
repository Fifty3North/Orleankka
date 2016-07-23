﻿using System;
using System.Runtime.Remoting.Messaging;

using NUnit.Framework;

namespace Orleankka.Features
{
    using CSharp;
	
    namespace Reentrant_messages
    {
        using Meta;
        using Testing;

        [Serializable]
        class RegularMessage : Query<bool>
        {}

        [Serializable]
        class ReentrantMessage : Query<bool>
        {}

        [Reentrant(typeof(ReentrantMessage))]
        class TestActor : Actor
        {
            bool On(RegularMessage x)   => ReceivedReentrant(x);
            bool On(ReentrantMessage x) => ReceivedReentrant(x);

            static bool ReceivedReentrant(object message) => 
                CallContext.LogicalGetData("LastMessageReceivedReentrant") == message;
        }
                
        [RequiresSilo]
        public class Tests
        {
            IActorSystem system;

            [SetUp]
            public void SetUp()
            {
                system = TestActorSystem.Instance;
            }

            [Test]
            public async void Could_be_defined_via_attribute()
            {
                var actor = system.FreshActorOf<TestActor>();
                Assert.That(await actor.Ask(new RegularMessage()), Is.False);
                Assert.That(await actor.Ask(new ReentrantMessage()), Is.True);
            }
        }
    }
}