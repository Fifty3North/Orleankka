﻿using System;
using System.Linq;

using NUnit.Framework;

namespace Orleankka.Features
{
    using CSharp;
    using Testing;

    [TestFixture]
    [RequiresSilo]
    public class Type_codes
    {
        IActorSystem system;

        [SetUp]
        public void SetUp()
        {
            system = TestActorSystem.Instance;
        }

        [Test]
        public void Type_name_as_default_could_be_overwritten_with_type_code_attribute()
        {
            var actor1 = system.ActorOf<TypeCode1.TestActor>("id");
            var actor2 = system.ActorOf<TypeCode2.TestActor>("id");

            Assert.That(actor1.Path, Is.EqualTo(ActorPath.Parse("T1:id")));
            Assert.That(actor2.Path, Is.EqualTo(ActorPath.Parse("T2:id")));
        }
    }

    namespace TypeCode1
    {
        [ActorTypeCode("T1")]
        class TestActor : Actor
        {}
    }

    namespace TypeCode2
    {
        [ActorTypeCode("T2")]
        class TestActor : Actor
        {}
    }
}