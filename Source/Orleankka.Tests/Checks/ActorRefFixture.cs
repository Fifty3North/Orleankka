﻿using NUnit.Framework;

namespace Orleankka.Checks
{
    [TestFixture]
    public class ActorRefFixture
    {
        [Test]
        public void Equatable_by_path()
        {
            var path = ActorPath.From("T", "42");

            var ref1 = new ActorRef(path, null, null);
            var ref2 = new ActorRef(path, null, null);
            
            Assert.True(ref1 == ref2);
            Assert.True(ref1.Equals(ref2));
        }
    }
}
